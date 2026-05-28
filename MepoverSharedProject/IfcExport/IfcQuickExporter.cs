using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.TopologyResource;
using Xbim.Ifc2x3.UtilityResource;
using Xbim.IO;

namespace IfcExport
{
    /// <summary>
    /// Synchronous IFC2x3 exporter that writes Revit elements with geometry.
    /// Runs entirely on the Revit main thread inside an ExternalEvent handler.
    ///
    /// Geometry strategy: get_Geometry(Fine) ? extract Solid objects
    /// (unwrapping GeometryInstance) ? Face.Triangulate() ? IfcFacetedBrep.
    /// Coordinates are already in Revit world space; multiply by 0.3048 for metres.
    /// </summary>
    internal static class IfcQuickExporter
    {
        private const double FeetToMetres = 0.3048;

        // ------------------------------------------------------------------ public entry point

        /// <param name="viewId">
        /// When non-null, only elements visible in that view are exported and the view name is
        /// appended to the output filename (unless <paramref name="callerOutputPath"/> is set).
        /// Pass <c>null</c> for a full-model export.
        /// </param>
        /// <param name="callerOutputPath">
        /// When non-null, write the IFC directly to this path instead of deriving a filename
        /// from the document and view name. The caller is responsible for ensuring the directory exists.
        /// </param>
        public static string Export(UIApplication uiApp, string destinationFolder,
            ElementId viewId = null, string callerOutputPath = null)
        {
            try
            {
                Document doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                    return "Error: No active document.";

                string outputPath;
                if (!string.IsNullOrWhiteSpace(callerOutputPath))
                {
                    outputPath = callerOutputPath;
                    string dir = Path.GetDirectoryName(callerOutputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(destinationFolder))
                        return "Error: No destination folder selected.";

                    if (!Directory.Exists(destinationFolder))
                        Directory.CreateDirectory(destinationFolder);

                    string docTitle = string.IsNullOrEmpty(doc.Title) ? "export" : doc.Title;
                    string docName  = MakeSafeFileName(Path.GetFileNameWithoutExtension(docTitle));

                    if (viewId != null && viewId != ElementId.InvalidElementId)
                    {
                        string viewName = MakeSafeFileName(
                            (doc.GetElement(viewId) as View)?.Name ?? viewId.ToString());
                        outputPath = Path.Combine(destinationFolder, docName + "_" + viewName + ".ifc");
                    }
                    else
                    {
                        outputPath = Path.Combine(destinationFolder, docName + ".ifc");
                    }
                }

                var credentials = new XbimEditorCredentials
                {
                    ApplicationDevelopersName = "MEPover",
                    ApplicationFullName       = "MEPover IFC Quick Exporter",
                    ApplicationIdentifier     = "MEPoverIFC",
                    ApplicationVersion        = "1.0",
                    EditorsFamilyName         = "MEPover",
                    EditorsGivenName          = "IFC",
                    EditorsOrganisationName   = "MEPover"
                };

                int exportedCount;
                using (var store = IfcStore.Create(credentials, XbimSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel))
                {
                    using (var txn = store.BeginTransaction("Build IFC model"))
                    {
                        var hierarchy = BuildProjectHierarchy(store, doc);
                        exportedCount = ExportElements(store, doc, hierarchy, viewId);
                        txn.Commit();
                    }

                    store.SaveAs(outputPath, Xbim.IO.StorageType.Ifc);
                }

                if (exportedCount == 0)
                    return "Warning:0:" + outputPath;

                return outputPath;
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ project hierarchy

        private struct Hierarchy
        {
            public IfcGeometricRepresentationContext ModelContext;
            public Dictionary<ElementId, IfcBuildingStorey> LevelMap;
            public IfcBuildingStorey FallbackStorey;
        }

        private static Hierarchy BuildProjectHierarchy(IfcStore store, Document doc)
        {
            var i = store.Instances;

            // IfcProject
            var project = i.New<IfcProject>(p =>
            {
                p.Name     = doc.Title ?? "Revit Project";
                p.GlobalId = NewGuid();
                p.LongName = doc.Title ?? "Revit Project";
            });

            // Units � length (metres) and plane angle (radians) are both required.
            project.UnitsInContext = i.New<IfcUnitAssignment>(u =>
            {
                u.Units.Add(i.New<IfcSIUnit>(s =>
                {
                    s.UnitType = IfcUnitEnum.LENGTHUNIT;
                    s.Name     = IfcSIUnitName.METRE;
                }));
                u.Units.Add(i.New<IfcSIUnit>(s =>
                {
                    s.UnitType = IfcUnitEnum.PLANEANGLEUNIT;
                    s.Name     = IfcSIUnitName.RADIAN;
                }));
            });

            // Representation contexts � required for geometry to be valid.
            var modelContext = i.New<IfcGeometricRepresentationContext>(c =>
            {
                c.ContextType              = "Model";
                c.ContextIdentifier        = "Body";
                c.CoordinateSpaceDimension = 3;
                c.Precision                = 0.00001;
                c.WorldCoordinateSystem    = i.New<IfcAxis2Placement3D>(a =>
                    a.Location = i.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0)));
            });
            project.RepresentationContexts.Add(modelContext);
            project.RepresentationContexts.Add(i.New<IfcGeometricRepresentationContext>(c =>
            {
                c.ContextType              = "Plan";
                c.ContextIdentifier        = "Annotation";
                c.CoordinateSpaceDimension = 2;
                c.Precision                = 0.00001;
                c.WorldCoordinateSystem    = i.New<IfcAxis2Placement2D>(a =>
                    a.Location = i.New<IfcCartesianPoint>(p => p.SetXY(0, 0)));
            }));

            // IfcSite ? IfcBuilding
            var site = i.New<IfcSite>(s =>
            {
                s.Name            = "Default Site";
                s.GlobalId        = NewGuid();
                s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                s.ObjectPlacement = WorldPlacement(store);
            });
            project.AddSite(site);

            var building = i.New<IfcBuilding>(b =>
            {
                b.Name            = doc.Title ?? "Building";
                b.GlobalId        = NewGuid();
                b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                b.ObjectPlacement = WorldPlacement(store);
            });
            site.AddBuilding(building);

            // IfcBuildingStorey per Revit Level
            var buildingAgg = i.New<IfcRelAggregates>(r =>
            {
                r.GlobalId       = NewGuid();
                r.RelatingObject = building;
            });

            var levelMap = new Dictionary<ElementId, IfcBuildingStorey>();

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            foreach (Level level in levels)
            {
                double elevM = level.Elevation * FeetToMetres;
                var storey = i.New<IfcBuildingStorey>(s =>
                {
                    s.Name            = level.Name;
                    s.GlobalId        = NewGuid();
                    s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    s.Elevation       = elevM;
                    s.ObjectPlacement = ElevationPlacement(store, elevM);
                });
                buildingAgg.RelatedObjects.Add(storey);
                levelMap[level.Id] = storey;
            }

            // Fallback storey in case a model has no levels.
            IfcBuildingStorey fallback = levelMap.Count > 0
                ? levelMap.Values.First()
                : i.New<IfcBuildingStorey>(s =>
                {
                    s.Name            = "Ground Floor";
                    s.GlobalId        = NewGuid();
                    s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    s.Elevation       = 0;
                    s.ObjectPlacement = WorldPlacement(store);
                    buildingAgg.RelatedObjects.Add(s);
                });

            return new Hierarchy
            {
                ModelContext  = modelContext,
                LevelMap      = levelMap,
                FallbackStorey = fallback
            };
        }

        // ------------------------------------------------------------------ element export

        private static int ExportElements(IfcStore store, Document doc, Hierarchy h,
            ElementId viewId = null)
        {
            // One IfcRelContainedInSpatialStructure per storey, created on demand.
            var containsMap = new Dictionary<IfcBuildingStorey, IfcRelContainedInSpatialStructure>();

            // For view-scoped exports, set Options.View so get_Geometry respects the view's
            // cut plane, phase, and visibility/graphics overrides.
            View scopedView = (viewId != null && viewId != ElementId.InvalidElementId)
                ? doc.GetElement(viewId) as View
                : null;
            var geomOptions = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                View        = scopedView
            };

            // View-scoped export: only elements visible in the specified view.
            // Full-model export: view-independent elements only (annotations excluded).
            List<Element> elements;
            if (scopedView != null)
            {
                elements = new FilteredElementCollector(doc, viewId)
                    .WhereElementIsNotElementType()
                    .ToList();
            }
            else
            {
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent()
                    .ToList();
            }

            int exported = 0;
            foreach (Element elem in elements)
            {
                try
                {
                    if (ExportOneElement(store, doc, elem, h, containsMap, geomOptions))
                        exported++;
                }
                catch
                {
                    // skip bad element
                }
            }
            return exported;
        }

        private static bool ExportOneElement(
            IfcStore store,
            Document doc,
            Element elem,
            Hierarchy h,
            Dictionary<IfcBuildingStorey, IfcRelContainedInSpatialStructure> containsMap,
            Options geomOptions)
        {
            // Extract all solids from this element (unwrap GeometryInstances).
            var solids = ExtractSolids(elem, geomOptions);
            if (solids.Count == 0) return false;

            // Build one IfcFacetedBrep per solid and collect them.
            var breps = new List<IfcFacetedBrep>();
            foreach (Solid solid in solids)
            {
                IfcFacetedBrep brep = TryCreateFacetedBrep(store, solid);
                if (brep != null)
                    breps.Add(brep);
            }
            if (breps.Count == 0) return false;

            var ifcInst = store.Instances;

            // Shape representation � all breps share one IfcShapeRepresentation.
            var shape = ifcInst.New<IfcShapeRepresentation>(s =>
            {
                s.ContextOfItems          = h.ModelContext;
                s.RepresentationIdentifier = "Body";
                s.RepresentationType       = "Brep";
            });
            foreach (var brep in breps)
                shape.Items.Add(brep);

            // IfcBuildingElementProxy � generic IFC entity for any Revit element.
            var proxy = ifcInst.New<IfcBuildingElementProxy>(p =>
            {
                p.Name            = elem.Name ?? elem.Category?.Name ?? "Element";
                p.GlobalId        = NewGuid();
                p.ObjectPlacement = WorldPlacement(store);   // coords are already world-space
                p.Representation  = ifcInst.New<IfcProductDefinitionShape>(r =>
                    r.Representations.Add(shape));
            });

            // Assign to storey.
            IfcBuildingStorey storey = ResolveStorey(elem, doc, h);
            if (!containsMap.TryGetValue(storey, out IfcRelContainedInSpatialStructure rel))
            {
                rel = ifcInst.New<IfcRelContainedInSpatialStructure>(r =>
                {
                    r.GlobalId          = NewGuid();
                    r.RelatingStructure = storey;
                });
                containsMap[storey] = rel;
            }
            rel.RelatedElements.Add(proxy);
            return true;
        }

        // ------------------------------------------------------------------ geometry extraction

        /// <summary>
        /// Returns all non-empty solids from an element, unwrapping GeometryInstances
        /// so that vertices are in Revit world coordinates.
        /// </summary>
        private static List<Solid> ExtractSolids(Element elem, Options opts)
        {
            var result = new List<Solid>();
            GeometryElement geomElem = elem.get_Geometry(opts);
            if (geomElem == null) return result;

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
                    // GetInstanceGeometry() applies the instance transform ? world coordinates.
                    CollectSolids(gi.GetInstanceGeometry(), result);
                }
            }
        }

        /// <summary>
        /// Triangulates all faces of a solid and writes them as an IfcFacetedBrep.
        /// Returns null if the solid produces no valid triangles.
        /// </summary>
        private static IfcFacetedBrep TryCreateFacetedBrep(IfcStore store, Solid solid)
        {
            var ifcFaces = new List<IfcFace>();

            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null || mesh.NumTriangles == 0) continue;

                for (int t = 0; t < mesh.NumTriangles; t++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(t);

                    // One IfcFace per triangle � three IfcCartesianPoints in an IfcPolyLoop.
                    var ifcFace = store.Instances.New<IfcFace>(f =>
                        f.Bounds.Add(store.Instances.New<IfcFaceOuterBound>(b =>
                        {
                            b.Orientation = true;
                            b.Bound = store.Instances.New<IfcPolyLoop>(loop =>
                            {
                                loop.Polygon.Add(ToIfcPoint(store, triangle.get_Vertex(0)));
                                loop.Polygon.Add(ToIfcPoint(store, triangle.get_Vertex(1)));
                                loop.Polygon.Add(ToIfcPoint(store, triangle.get_Vertex(2)));
                            });
                        })));

                    ifcFaces.Add(ifcFace);
                }
            }

            if (ifcFaces.Count == 0) return null;

            return store.Instances.New<IfcFacetedBrep>(brep =>
            {
                brep.Outer = store.Instances.New<IfcClosedShell>(shell =>
                {
                    foreach (var f in ifcFaces)
                        shell.CfsFaces.Add(f);
                });
            });
        }

        // ------------------------------------------------------------------ storey lookup

        /// <summary>
        /// Tries several level parameters to find which storey an element belongs to.
        /// Falls back to the lowest storey when nothing matches.
        /// </summary>
        private static IfcBuildingStorey ResolveStorey(Element elem, Document doc, Hierarchy h)
        {
            ElementId levelId = GetLevelId(elem);
            if (levelId != null && levelId != ElementId.InvalidElementId
                && h.LevelMap.TryGetValue(levelId, out IfcBuildingStorey storey))
                return storey;

            return h.FallbackStorey;
        }

        private static ElementId GetLevelId(Element elem)
        {
            // Try the most common level parameters in order.
            BuiltInParameter[] candidates =
            {
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,        // most hosted elements
                BuiltInParameter.FAMILY_LEVEL_PARAM,          // loadable families
                BuiltInParameter.RBS_START_LEVEL_PARAM,       // MEP (ducts, pipes)
                BuiltInParameter.WALL_BASE_CONSTRAINT,        // walls
                BuiltInParameter.STAIRS_BASE_LEVEL_PARAM      // stairs
            };

            foreach (var bip in candidates)
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

        // ------------------------------------------------------------------ placement helpers

        private static IfcLocalPlacement WorldPlacement(IfcStore store) =>
            store.Instances.New<IfcLocalPlacement>(lp =>
                lp.RelativePlacement = store.Instances.New<IfcAxis2Placement3D>(a =>
                    a.Location = store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0))));

        private static IfcLocalPlacement ElevationPlacement(IfcStore store, double elevM) =>
            store.Instances.New<IfcLocalPlacement>(lp =>
                lp.RelativePlacement = store.Instances.New<IfcAxis2Placement3D>(a =>
                    a.Location = store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, elevM))));

        // ------------------------------------------------------------------ IFC point helper

        private static IfcCartesianPoint ToIfcPoint(IfcStore store, XYZ pt) =>
            store.Instances.New<IfcCartesianPoint>(p =>
                p.SetXYZ(pt.X * FeetToMetres, pt.Y * FeetToMetres, pt.Z * FeetToMetres));

        // ------------------------------------------------------------------ GUID + filename

        private static IfcGloballyUniqueId NewGuid() =>
            IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
