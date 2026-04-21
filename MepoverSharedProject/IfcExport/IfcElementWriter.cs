using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.TopologyResource;
using Xbim.Ifc2x3.UtilityResource;

namespace IfcExport
{
    /// <summary>
    /// Exports one Revit element per call into the session's live IfcStore.
    ///
    /// Each call opens its own Xbim transaction, writes the element, and commits.
    /// This keeps individual ticks short and makes partial exports coherent on disk.
    ///
    /// Guard rails:
    ///   - Never opens a Revit transaction — read-only Revit API use only.
    ///   - Skips gracefully on null/empty geometry or any exception.
    ///   - All Revit API calls must happen on the main thread (Idling handler).
    /// </summary>
    internal static class IfcElementWriter
    {
        private const double FeetToMetres = 0.3048;

        private static readonly Options GeomOptions = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false
        };

        // ------------------------------------------------------------------ public entry point

        /// <summary>
        /// Exports <paramref name="element"/> into <paramref name="session"/>.Store.
        /// Updates ExportStateMap and ElementIdIndex on success.
        /// </summary>
        /// <returns>The IFC GUID assigned, or null if the element was skipped.</returns>
        public static string WriteElement(Element element, IfcExportSession session)
        {
            if (element == null || session.Store == null)
                return null;
            try
            {
                List<Solid> solids = ExtractSolids(element);
                if (solids.Count == 0) return null;

                string ifcGuid;

                // One Xbim transaction per element — keeps each tick atomic.
                // All store.Instances.New<> calls must happen inside the transaction.
                using (var txn = session.Store.BeginTransaction("WriteElement"))
                {
                    var i = session.Store.Instances;

                    var breps = new List<IfcFacetedBrep>();
                    foreach (Solid solid in solids)
                    {
                        IfcFacetedBrep brep = TryCreateFacetedBrep(session.Store, solid);
                        if (brep != null) breps.Add(brep);
                    }
                    if (breps.Count == 0)
                    {
                        txn.RollBack();
                        return null;
                    }

                    var shape = i.New<IfcShapeRepresentation>(s =>
                    {
                        s.ContextOfItems = session.ModelContext;
                        s.RepresentationIdentifier = "Body";
                        s.RepresentationType = "Brep";
                    });
                    foreach (var brep in breps)
                        shape.Items.Add(brep);

                    var guid = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
                    ifcGuid = guid.ToString();

                    var proxy = i.New<IfcBuildingElementProxy>(p =>
                    {
                        p.Name = element.Name ?? element.Category?.Name ?? "Element";
                        p.GlobalId = guid;
                        p.ObjectPlacement = WorldPlacement(session.Store);
                        p.Representation = i.New<IfcProductDefinitionShape>(r =>
                            r.Representations.Add(shape));
                    });

                    // Assign to the correct storey.
                    IfcBuildingStorey storey = ResolveStorey(element, session);
                    if (!session.ContainsMap.TryGetValue(storey,
                        out IfcRelContainedInSpatialStructure rel))
                    {
                        rel = i.New<IfcRelContainedInSpatialStructure>(r =>
                        {
                            r.GlobalId = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
                            r.RelatingStructure = storey;
                        });
                        session.ContainsMap[storey] = rel;
                    }
                    rel.RelatedElements.Add(proxy);

                    txn.Commit();
                }

                // Update the session change-tracking maps.
                session.ExportStateMap[element.UniqueId] = ifcGuid;
                session.ElementIdIndex[element.Id] = element.UniqueId;

                return ifcGuid;
            }
            catch (Exception ex)
            {
                // Log to the VS Output window so geometry failures are visible during debugging.
                System.Diagnostics.Debug.WriteLine(
                    string.Format("IfcElementWriter: skipped '{0}' (id {1}): {2}",
                        element?.Name, element?.Id, ex.Message));
                return null;
            }
        }

        // ------------------------------------------------------------------ geometry extraction

        private static List<Solid> ExtractSolids(Element elem)
        {
            var result = new List<Solid>();
            GeometryElement geomElem = elem.get_Geometry(GeomOptions);
            if (geomElem != null)
                CollectSolids(geomElem, result);
            return result;
        }

        private static void CollectSolids(GeometryElement geomElem, List<Solid> result)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid)
                {
                    if (solid.Faces.Size > 0 && solid.Volume > 1e-9)
                        result.Add(solid);
                }
                else if (obj is GeometryInstance gi)
                {
                    // GetInstanceGeometry() applies the instance transform → world coordinates.
                    CollectSolids(gi.GetInstanceGeometry(), result);
                }
            }
        }

        private static IfcFacetedBrep TryCreateFacetedBrep(Xbim.Ifc.IfcStore store, Solid solid)
        {
            var ifcFaces = new List<IfcFace>();

            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null || mesh.NumTriangles == 0) continue;

                for (int t = 0; t < mesh.NumTriangles; t++)
                {
                    MeshTriangle tri = mesh.get_Triangle(t);
                    ifcFaces.Add(store.Instances.New<IfcFace>(f =>
                        f.Bounds.Add(store.Instances.New<IfcFaceOuterBound>(b =>
                        {
                            b.Orientation = true;
                            b.Bound = store.Instances.New<IfcPolyLoop>(loop =>
                            {
                                loop.Polygon.Add(ToIfcPoint(store, tri.get_Vertex(0)));
                                loop.Polygon.Add(ToIfcPoint(store, tri.get_Vertex(1)));
                                loop.Polygon.Add(ToIfcPoint(store, tri.get_Vertex(2)));
                            });
                        }))));
                }
            }

            if (ifcFaces.Count == 0) return null;

            return store.Instances.New<IfcFacetedBrep>(brep =>
                brep.Outer = store.Instances.New<IfcClosedShell>(shell =>
                {
                    foreach (var f in ifcFaces)
                        shell.CfsFaces.Add(f);
                }));
        }

        // ------------------------------------------------------------------ storey lookup

        private static IfcBuildingStorey ResolveStorey(Element elem, IfcExportSession session)
        {
            ElementId levelId = GetLevelId(elem);
            if (levelId != null && levelId != ElementId.InvalidElementId
                && session.LevelMap.TryGetValue(levelId, out IfcBuildingStorey storey))
                return storey;

            return session.FallbackStorey;
        }

        private static ElementId GetLevelId(Element elem)
        {
            BuiltInParameter[] candidates =
            {
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.RBS_START_LEVEL_PARAM,   // MEP (ducts, pipes)
                BuiltInParameter.WALL_BASE_CONSTRAINT,    // walls
                BuiltInParameter.STAIRS_BASE_LEVEL_PARAM
            };

            foreach (BuiltInParameter bip in candidates)
            {
                Parameter p = elem.get_Parameter(bip);
                if (p != null && p.StorageType == Autodesk.Revit.DB.StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                        return id;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ IFC helpers

        private static IfcLocalPlacement WorldPlacement(Xbim.Ifc.IfcStore store) =>
            store.Instances.New<IfcLocalPlacement>(lp =>
                lp.RelativePlacement = store.Instances.New<IfcAxis2Placement3D>(a =>
                    a.Location = store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0))));

        private static IfcCartesianPoint ToIfcPoint(Xbim.Ifc.IfcStore store, XYZ pt) =>
            store.Instances.New<IfcCartesianPoint>(p =>
                p.SetXYZ(pt.X * FeetToMetres, pt.Y * FeetToMetres, pt.Z * FeetToMetres));
    }
}
