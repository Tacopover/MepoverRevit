using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.PropertyResource;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.SharedBldgElements;
using Xbim.Ifc2x3.SharedBldgServiceElements;
using Xbim.Ifc2x3.SharedFacilitiesElements;
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

                    var placement    = WorldPlacement(session.Store);
                    var productShape = i.New<IfcProductDefinitionShape>(r =>
                        r.Representations.Add(shape));

                    IfcElement entity = CreateEntity(session.Store, element, guid, placement, productShape);
                    AttachPropertySets(session.Store, entity, element, session.PsetMappings);

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
                    rel.RelatedElements.Add(entity);

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

        // ------------------------------------------------------------------ extraction (Revit main thread only)

        /// <summary>
        /// Reads all Revit API data needed to write an IFC element and packages it into a plain DTO.
        /// Returns null when the element has no exportable solid geometry or on any error.
        /// MUST be called on the Revit main thread — uses get_Geometry and face.Triangulate.
        /// The returned DTO contains no Revit API objects and is safe to enqueue for background processing.
        /// </summary>
        public static ElementGeometryDto ExtractDto(Element element, IList<PsetMappingBlock> psetMappings)
        {
            if (element == null) return null;
            try
            {
                List<Solid> solids = ExtractSolids(element);
                if (solids.Count == 0) return null;

                var solidData = new List<List<Triangle3d>>();
                foreach (Solid solid in solids)
                {
                    var triangles = new List<Triangle3d>();
                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh = face.Triangulate();
                        if (mesh == null || mesh.NumTriangles == 0) continue;
                        for (int t = 0; t < mesh.NumTriangles; t++)
                        {
                            MeshTriangle tri = mesh.get_Triangle(t);
                            XYZ v0 = tri.get_Vertex(0), v1 = tri.get_Vertex(1), v2 = tri.get_Vertex(2);
                            triangles.Add(new Triangle3d(
                                v0.X * FeetToMetres, v0.Y * FeetToMetres, v0.Z * FeetToMetres,
                                v1.X * FeetToMetres, v1.Y * FeetToMetres, v1.Z * FeetToMetres,
                                v2.X * FeetToMetres, v2.Y * FeetToMetres, v2.Z * FeetToMetres));
                        }
                    }
                    if (triangles.Count > 0)
                        solidData.Add(triangles);
                }
                if (solidData.Count == 0) return null;

                return new ElementGeometryDto
                {
                    UniqueId      = element.UniqueId,
                    ElementId     = element.Id,
                    Name          = element.Name ?? element.Category?.Name ?? "Element",
                    CategoryId    = element.Category?.Id?.IntegerValue ?? 0,
                    LevelId       = GetLevelId(element),
                    ExportToIfcAs = GetExportToIfcAs(element),
                    Solids        = solidData,
                    PropertySets  = ExtractPsetData(element, psetMappings)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("IfcElementWriter.ExtractDto skipped '{0}' (id {1}): {2}",
                        element?.Name, element?.Id, ex.Message));
                return null;
            }
        }

        private static List<PsetData> ExtractPsetData(Element element, IList<PsetMappingBlock> mappings)
        {
            var result = new List<PsetData>();
            if (mappings == null || mappings.Count == 0) return result;

            foreach (PsetMappingBlock block in mappings)
            {
                var resolved   = new System.Collections.Generic.HashSet<string>();
                var properties = new List<PropertyData>();

                foreach (PsetPropertyMapping mapping in block.Properties)
                {
                    if (resolved.Contains(mapping.IfcPropertyName)) continue;
                    string value = TryGetRevitParameterValue(element, mapping.RevitParameterName);
                    if (string.IsNullOrEmpty(value)) continue;
                    resolved.Add(mapping.IfcPropertyName);
                    properties.Add(new PropertyData
                    {
                        IfcPropertyName = mapping.IfcPropertyName,
                        DataType        = mapping.DataType,
                        Value           = value
                    });
                }

                if (properties.Count > 0)
                    result.Add(new PsetData
                    {
                        PsetName       = block.PsetName,
                        IfcTypeFilters = new List<string>(block.IfcTypeFilters),
                        Properties     = properties
                    });
            }
            return result;
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

        // ------------------------------------------------------------------ entity type mapping

        // Maps "Export To IFC As" parameter values to IFC2x3 entity factories (case-insensitive).
        // IFC4 names (IfcDuctSegment, IfcPipeSegment, etc.) map to their IFC2x3 occurrence equivalents
        // because IFC2x3 only has generic flow element occurrences (IfcFlowSegment, IfcFlowFitting…).
        private static readonly Dictionary<string, Func<Xbim.Ifc.IfcStore, IfcElement>> IfcTypeNameMap =
            new Dictionary<string, Func<Xbim.Ifc.IfcStore, IfcElement>>(StringComparer.OrdinalIgnoreCase)
            {
                // Architectural
                { "IfcWall",                 s => s.Instances.New<IfcWall>() },
                { "IfcWallStandardCase",     s => s.Instances.New<IfcWallStandardCase>() },
                { "IfcSlab",                 s => s.Instances.New<IfcSlab>() },
                { "IfcCovering",             s => s.Instances.New<IfcCovering>() },
                { "IfcColumn",               s => s.Instances.New<IfcColumn>() },
                { "IfcBeam",                 s => s.Instances.New<IfcBeam>() },
                { "IfcDoor",                 s => s.Instances.New<IfcDoor>() },
                { "IfcWindow",               s => s.Instances.New<IfcWindow>() },
                { "IfcRoof",                 s => s.Instances.New<IfcRoof>() },
                { "IfcStair",                s => s.Instances.New<IfcStair>() },
                { "IfcRailing",              s => s.Instances.New<IfcRailing>() },
                { "IfcRamp",                 s => s.Instances.New<IfcRamp>() },
                { "IfcCurtainWall",          s => s.Instances.New<IfcCurtainWall>() },
                { "IfcPlate",                s => s.Instances.New<IfcPlate>() },
                { "IfcFurnishingElement",    s => s.Instances.New<IfcFurnishingElement>() },
                { "IfcTransportElement",     s => s.Instances.New<IfcTransportElement>() },
                { "IfcBuildingElementProxy", s => s.Instances.New<IfcBuildingElementProxy>() },
                { "IfcElementAssembly",      s => s.Instances.New<IfcElementAssembly>() },
                // MEP — IFC2x3 occurrence types (no IfcDuctSegment/IfcPipeSegment occurrences in IFC2x3)
                { "IfcFlowSegment",          s => s.Instances.New<IfcFlowSegment>() },
                { "IfcDuctSegment",          s => s.Instances.New<IfcFlowSegment>() },   // IFC4 name → IFC2x3
                { "IfcPipeSegment",          s => s.Instances.New<IfcFlowSegment>() },   // IFC4 name → IFC2x3
                { "IfcFlowFitting",          s => s.Instances.New<IfcFlowFitting>() },
                { "IfcDuctFitting",          s => s.Instances.New<IfcFlowFitting>() },   // IFC4 name → IFC2x3
                { "IfcPipeFitting",          s => s.Instances.New<IfcFlowFitting>() },   // IFC4 name → IFC2x3
                { "IfcFlowTerminal",         s => s.Instances.New<IfcFlowTerminal>() },
                { "IfcFlowMovingDevice",     s => s.Instances.New<IfcFlowMovingDevice>() },
                { "IfcFlowController",       s => s.Instances.New<IfcFlowController>() },
            };

        // Category integer ID → IFC2x3 entity factory.
        // Derived from MEPover IFC export mapping.txt — first entry (blank subcategory) per category.
        private static readonly Dictionary<int, Func<Xbim.Ifc.IfcStore, IfcElement>> CategoryEntityMap =
            new Dictionary<int, Func<Xbim.Ifc.IfcStore, IfcElement>>
            {
                // Architectural
                { -2000011, s => s.Instances.New<IfcWall>() },               // Walls
                { -2000032, s => s.Instances.New<IfcSlab>() },               // Floors
                { -2000038, s => s.Instances.New<IfcCovering>() },           // Ceilings
                { -2000100, s => s.Instances.New<IfcColumn>() },             // Columns
                { -2000023, s => s.Instances.New<IfcDoor>() },               // Doors
                { -2000014, s => s.Instances.New<IfcWindow>() },             // Windows
                { -2000035, s => s.Instances.New<IfcRoof>() },               // Roofs
                { -2000120, s => s.Instances.New<IfcStair>() },              // Stairs
                { -2000126, s => s.Instances.New<IfcRailing>() },            // Railings
                { -2000180, s => s.Instances.New<IfcRamp>() },               // Ramps
                { -2000170, s => s.Instances.New<IfcCurtainWall>() },        // Curtain Panels
                { -2000340, s => s.Instances.New<IfcCurtainWall>() },        // Curtain Systems
                { -2000171, s => s.Instances.New<IfcCurtainWall>() },        // Curtain Wall Mullions
                { -2000090, s => s.Instances.New<IfcCurtainWall>() },        // Ruled Curtain System
                { -2001300, s => s.Instances.New<IfcSlab>() },               // Structural Foundations
                { -2001330, s => s.Instances.New<IfcColumn>() },             // Structural Columns
                { -2001354, s => s.Instances.New<IfcPlate>() },              // Structural Stiffeners
                { -2000080, s => s.Instances.New<IfcFurnishingElement>() },  // Furniture
                { -2001000, s => s.Instances.New<IfcFurnishingElement>() },  // Casework
                { -2001052, s => s.Instances.New<IfcTransportElement>() },   // Vertical Circulation
                { -2000267, s => s.Instances.New<IfcElementAssembly>() },    // Assemblies
                { -2000269, s => s.Instances.New<IfcBuildingElementProxy>() },// Parts
                { -2008231, s => s.Instances.New<IfcBuildingElementProxy>() },// MEP Ancillary Framing
                { -2006130, s => s.Instances.New<IfcSlab>() },               // Abutments
                { -2006131, s => s.Instances.New<IfcColumn>() },             // Piers
                { -2006135, s => s.Instances.New<IfcSlab>() },               // Bridge Decks
                // MEP - Duct (IFC2x3: IfcFlowSegment for segments, IfcFlowFitting for fittings)
                { -2008000, s => s.Instances.New<IfcFlowSegment>() },        // Ducts
                { -2008010, s => s.Instances.New<IfcFlowFitting>() },        // Duct Fittings
                { -2008016, s => s.Instances.New<IfcFlowFitting>() },        // Duct Accessories
                { -2008020, s => s.Instances.New<IfcFlowSegment>() },        // Flex Ducts
                { -2008123, s => s.Instances.New<IfcCovering>() },           // Duct Insulations
                { -2008124, s => s.Instances.New<IfcCovering>() },           // Duct Linings
                { -2008160, s => s.Instances.New<IfcFlowSegment>() },        // Duct Placeholders
                { -2008193, s => s.Instances.New<IfcFlowSegment>() },        // MEP Fabrication Ductwork
                // MEP - Pipe
                { -2008044, s => s.Instances.New<IfcFlowSegment>() },        // Pipes
                { -2008049, s => s.Instances.New<IfcFlowFitting>() },        // Pipe Fittings
                { -2008050, s => s.Instances.New<IfcFlowSegment>() },        // Flex Pipes
                { -2008055, s => s.Instances.New<IfcFlowController>() },     // Pipe Accessories (valves)
                { -2008122, s => s.Instances.New<IfcCovering>() },           // Pipe Insulations
                { -2008161, s => s.Instances.New<IfcFlowSegment>() },        // Pipe Placeholders
                { -2008208, s => s.Instances.New<IfcFlowSegment>() },        // MEP Fabrication Pipework
                // MEP - Equipment & devices
                { -2008013, s => s.Instances.New<IfcFlowTerminal>() },       // Air Terminals
                { -2001140, s => s.Instances.New<IfcFlowMovingDevice>() },   // Mechanical Equipment
                { -2008234, s => s.Instances.New<IfcFlowTerminal>() },       // Plumbing Equipment
                { -2001160, s => s.Instances.New<IfcFlowTerminal>() },       // Plumbing Fixtures
                { -2001040, s => s.Instances.New<IfcFlowTerminal>() },       // Electrical Equipment
                { -2001060, s => s.Instances.New<IfcFlowTerminal>() },       // Electrical Fixtures
                { -2001120, s => s.Instances.New<IfcFlowTerminal>() },       // Lighting Fixtures
                { -2001049, s => s.Instances.New<IfcFlowTerminal>() },       // Fire Protection
                { -2008099, s => s.Instances.New<IfcFlowTerminal>() },       // Sprinklers
                { -2008085, s => s.Instances.New<IfcFlowTerminal>() },       // Fire Alarm Devices
                { -2008081, s => s.Instances.New<IfcFlowTerminal>() },       // Communication Devices
                { -2008083, s => s.Instances.New<IfcFlowTerminal>() },       // Data Devices
                { -2008087, s => s.Instances.New<IfcFlowTerminal>() },       // Lighting Devices
                { -2008079, s => s.Instances.New<IfcFlowTerminal>() },       // Security Devices
                { -2008077, s => s.Instances.New<IfcFlowTerminal>() },       // Nurse Call Devices
                { -2008075, s => s.Instances.New<IfcFlowTerminal>() },       // Telephone Devices
                { -2008232, s => s.Instances.New<IfcFlowController>() },     // Mechanical Control Devices
                // MEP - Cable containment
                { -2008130, s => s.Instances.New<IfcFlowSegment>() },        // Cable Trays
                { -2008126, s => s.Instances.New<IfcFlowFitting>() },        // Cable Tray Fittings
                { -2008132, s => s.Instances.New<IfcFlowSegment>() },        // Conduits
                { -2008128, s => s.Instances.New<IfcFlowFitting>() },        // Conduit Fittings
            };

        private static string GetExportToIfcAs(Element element)
        {
            const string paramName = "Export To IFC As";
            Parameter p = element.LookupParameter(paramName);
            if (p != null)
            {
                string v = p.AsString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            ElementType elemType = element.Document.GetElement(element.GetTypeId()) as ElementType;
            if (elemType != null)
            {
                p = elemType.LookupParameter(paramName);
                if (p != null)
                {
                    string v = p.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            return null;
        }

        private static IfcElement CreateEntity(
            Xbim.Ifc.IfcStore store,
            Element element,
            IfcGloballyUniqueId guid,
            IfcLocalPlacement placement,
            IfcProductDefinitionShape productShape)
        {
            string name  = element.Name ?? element.Category?.Name ?? "Element";
            int    catId = element.Category?.Id?.IntegerValue ?? 0;

            IfcElement e;
            string exportAs = GetExportToIfcAs(element);
            if (!string.IsNullOrEmpty(exportAs) && IfcTypeNameMap.TryGetValue(exportAs, out var namedFactory))
                e = namedFactory(store);
            else if (catId != 0 && CategoryEntityMap.TryGetValue(catId, out var catFactory))
                e = catFactory(store);
            else
                e = store.Instances.New<IfcBuildingElementProxy>();

            e.Name            = name;
            e.GlobalId        = guid;
            e.ObjectPlacement = placement;
            e.Representation  = productShape;
            return e;
        }

        // ------------------------------------------------------------------ property sets

        private static void AttachPropertySets(
            Xbim.Ifc.IfcStore store,
            IfcElement entity,
            Element element,
            System.Collections.Generic.IList<PsetMappingBlock> mappings)
        {
            if (mappings == null || mappings.Count == 0) return;

            var i = store.Instances;

            foreach (PsetMappingBlock block in mappings)
            {
                if (!EntityMatchesFilter(entity, block.IfcTypeFilters)) continue;

                var pset = i.New<IfcPropertySet>(ps =>
                {
                    ps.GlobalId = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
                    ps.Name     = block.PsetName;
                });

                // Fallback chain: track resolved IFC prop names so only the first
                // non-empty Revit parameter value wins for each IFC property name.
                var resolved = new System.Collections.Generic.HashSet<string>();

                foreach (PsetPropertyMapping mapping in block.Properties)
                {
                    if (resolved.Contains(mapping.IfcPropertyName)) continue;

                    string value = TryGetRevitParameterValue(element, mapping.RevitParameterName);
                    if (string.IsNullOrEmpty(value)) continue;

                    resolved.Add(mapping.IfcPropertyName);
                    string propName  = mapping.IfcPropertyName;
                    string dataType  = mapping.DataType;
                    string propValue = value;

                    pset.HasProperties.Add(i.New<IfcPropertySingleValue>(p =>
                    {
                        p.Name         = propName;
                        p.NominalValue = MakeIfcValue(dataType, propValue);
                    }));
                }

                if (pset.HasProperties.Count == 0) continue;

                i.New<IfcRelDefinesByProperties>(r =>
                {
                    r.GlobalId                   = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
                    r.RelatedObjects.Add(entity);
                    r.RelatingPropertyDefinition = pset;
                });
            }
        }

        private static bool EntityMatchesFilter(IfcElement entity, System.Collections.Generic.IList<string> typeFilters)
        {
            foreach (string filter in typeFilters)
            {
                if (filter == "IfcElement") return true;
                if (filter == "IfcWall"          && entity is IfcWall)          return true;
                if (filter == "IfcFlowSegment"   && entity is IfcFlowSegment)   return true;
                if (filter == "IfcBuildingElementProxy" && entity is IfcBuildingElementProxy) return true;
            }
            return false;
        }

        private static string TryGetRevitParameterValue(Element element, string paramName)
        {
            Parameter p = element.LookupParameter(paramName);
            if (p != null)
            {
                string v = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                if (!string.IsNullOrEmpty(v)) return v;
            }

            ElementType elemType = element.Document.GetElement(element.GetTypeId()) as ElementType;
            if (elemType != null)
            {
                p = elemType.LookupParameter(paramName);
                if (p != null)
                {
                    string v = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }

            return null;
        }

        private static IfcValue MakeIfcValue(string dataType, string value)
        {
            if (dataType == "Boolean" && bool.TryParse(value, out bool b))
                return new IfcBoolean(b);
            return new IfcText(value);
        }

        // ------------------------------------------------------------------ removal

        /// <summary>
        /// Removes the <see cref="IfcBuildingElementProxy"/> identified by <paramref name="ifcGuid"/>
        /// from the store, cascade-deleting its full geometry tree (shape representation, breps,
        /// faces, loops, cartesian points) and placement in one Xbim transaction.
        /// Also removes the proxy from every <see cref="IfcRelContainedInSpatialStructure"/> in
        /// <paramref name="session"/>.ContainsMap.
        /// </summary>
        public static void RemoveElement(string ifcGuid, IfcExportSession session)
        {
            if (string.IsNullOrEmpty(ifcGuid) || session?.Store == null)
                return;

            try
            {
                var entity = session.Store.Instances
                    .OfType<IfcElement>()
                    .FirstOrDefault(e => e.GlobalId.ToString() == ifcGuid);

                if (entity == null) return;

                using (var txn = session.Store.BeginTransaction("RemoveElement"))
                {
                    // Remove from all spatial-containment relationships first.
                    foreach (var rel in session.ContainsMap.Values)
                        rel.RelatedElements.Remove(entity);

                    // Cascade-delete geometry tree.
                    if (entity.Representation is IfcProductDefinitionShape defShape)
                    {
                        foreach (var rep in defShape.Representations.ToList())
                        {
                            if (rep is IfcShapeRepresentation shapeRep)
                            {
                                foreach (var item in shapeRep.Items.ToList())
                                {
                                    if (item is IfcFacetedBrep brep && brep.Outer != null)
                                    {
                                        foreach (var face in brep.Outer.CfsFaces.ToList())
                                        {
                                            foreach (var bound in face.Bounds.ToList())
                                            {
                                                if (bound.Bound is IfcPolyLoop loop)
                                                {
                                                    foreach (var pt in loop.Polygon.ToList())
                                                        session.Store.Delete(pt);
                                                    session.Store.Delete(loop);
                                                }
                                                session.Store.Delete(bound);
                                            }
                                            session.Store.Delete(face);
                                        }
                                        session.Store.Delete(brep.Outer);
                                        session.Store.Delete(brep);
                                    }
                                }
                                session.Store.Delete(shapeRep);
                            }
                        }
                        session.Store.Delete(defShape);
                    }

                    // Delete placement.
                    if (entity.ObjectPlacement is IfcLocalPlacement lp)
                    {
                        if (lp.RelativePlacement is IfcAxis2Placement3D ax)
                        {
                            if (ax.Location != null) session.Store.Delete(ax.Location);
                            session.Store.Delete(ax);
                        }
                        session.Store.Delete(lp);
                    }

                    session.Store.Delete(entity);
                    txn.Commit();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("IfcElementWriter.RemoveElement failed for guid '{0}': {1}",
                        ifcGuid, ex.Message));
            }
        }
    }
}
