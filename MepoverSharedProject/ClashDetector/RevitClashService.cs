using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using ClashDetector.Models;
using ClashDetector.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ClashDetector
{
    public class RevitClashService : IClashService
    {
        RequestHandler handler;
        ExternalEvent exEvent;
        private TaskCompletionSource<IReadOnlyList<ClashDto>> _runTcs;

        private Document doc;
        private Options geomOptions = new Options();
        public ClashSettings Settings { get; set; } = new ClashSettings();
        public UIApplication UIApp { get; private set; }
        public Dictionary<string, Document> DocumentMap { get; private set; }
        public Dictionary<string, RevitLinkInstance> RevitLinkInstanceMap { get; private set; }

        public RevitClashService(UIApplication uiapp)
        {
            UIApp = uiapp;
            doc = UIApp.ActiveUIDocument.Document;
            handler = new RequestHandler(this);
            exEvent = ExternalEvent.Create(handler);
        }

        public void Initialize()
        {
            PopulateLinkedModels();
            PopulateSettings();
        }

        // Collects element ids whose solid could not be built, so the run can report once
        // at the end instead of popping a MessageBox per element.
        private List<string> _solidErrors;

        public List<Clash> RunClashes()
        {
            ElementCollector collector = new ElementCollector(UIApp.ActiveUIDocument);
            Dictionary<string, List<Element>> ModelMap = collector.GetVisibleElements();

            List<string> docTitlesA = Settings.RevitModels1.Where(m => m.IsSelected).Select(m => m.Name).ToList();
            List<string> docTitlesB = Settings.RevitModels2.Where(m => m.IsSelected).Select(m => m.Name).ToList();

            List<Clash> clashes = new List<Clash>();
            _solidErrors = new List<string>();
            // De-duplicates unordered pairs so an A-B and the mirrored B-A test report once.
            HashSet<string> seenPairs = new HashSet<string>();

            foreach (string titleA in docTitlesA)
            {
                if (!ModelMap.TryGetValue(titleA, out List<Element> elementsA))
                {
                    continue;
                }
                if (!DocumentMap.TryGetValue(titleA, out Document documentA))
                {
                    continue;
                }
                Transform transformA = RevitLinkInstanceMap.TryGetValue(titleA, out RevitLinkInstance linkInstanceA)
                    ? linkInstanceA.GetTotalTransform()
                    : Transform.Identity;

                foreach (string titleB in docTitlesB)
                {
                    if (!ModelMap.TryGetValue(titleB, out List<Element> elementsB))
                    {
                        continue;
                    }
                    if (!DocumentMap.TryGetValue(titleB, out Document documentB))
                    {
                        continue;
                    }
                    Transform transformB = RevitLinkInstanceMap.TryGetValue(titleB, out RevitLinkInstance linkInstanceB)
                        ? linkInstanceB.GetTotalTransform()
                        : Transform.Identity;

                    bool sameModel = titleA == titleB;

                    foreach (Element elementA in elementsA)
                    {
                        foreach (Element elementB in elementsB)
                        {
                            // An element never clashes with itself.
                            if (sameModel && GetIdValue(elementA.Id) == GetIdValue(elementB.Id))
                            {
                                continue;
                            }

                            if (!seenPairs.Add(PairKey(titleA, elementA.Id, titleB, elementB.Id)))
                            {
                                continue;
                            }

                            Clash clash = getClash(documentA, documentB, elementA, elementB, transformA, transformB);
                            if (clash != null)
                            {
                                clashes.Add(clash);
                            }
                        }
                    }
                }
            }
            return clashes;
        }

        // Order-independent key for an element pair, so {A,B} and {B,A} collapse to one entry.
        private string PairKey(string titleA, ElementId idA, string titleB, ElementId idB)
        {
            string a = titleA + ":" + GetIdValue(idA);
            string b = titleB + ":" + GetIdValue(idB);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        // Symmetric, location-independent clash test: broad-phase on world-aligned bounding
        // boxes, narrow-phase on the boolean intersection of both solids. Either element may
        // lack a Location (floors, slabs, in-place families) — geometry, not Location, drives it.
        private Clash getClash(Document doc1, Document doc2, Element element1, Element element2, Transform transformLink1, Transform transformLink2)
        {
            BoundingBoxXYZ bbox1 = element1.get_BoundingBox(null);
            BoundingBoxXYZ bbox2 = element2.get_BoundingBox(null);
            if (bbox1 == null || bbox2 == null)
            {
                return null;
            }
            if (!BboxIntersects(TransformBbox(bbox1, transformLink1), TransformBbox(bbox2, transformLink2)))
            {
                return null;
            }

            Solid originSolid1 = TryGetSolid(element1, doc1) ?? SolidByBoundingBox(bbox1);
            Solid originSolid2 = TryGetSolid(element2, doc2) ?? SolidByBoundingBox(bbox2);
            if (originSolid1 == null || originSolid2 == null)
            {
                return null;
            }

            Solid solid1 = SolidUtils.CreateTransformed(originSolid1, transformLink1);
            Solid solid2 = SolidUtils.CreateTransformed(originSolid2, transformLink2);

            Solid diffSolid;
            try
            {
                diffSolid = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                _solidErrors?.Add($"{doc1?.Title}/{doc2?.Title}: intersection failed for {GetIdValue(element1.Id)} vs {GetIdValue(element2.Id)}");
                return null;
            }

            // Ignore vanishingly small overlaps (placeholders, touching faces).
            if (diffSolid == null || diffSolid.Volume <= 0.00001)
            {
                return null;
            }

            XYZ location_ft = diffSolid.ComputeCentroid();
            Clash clash = new Clash(doc1, doc2, element1, element2, location_ft, GetElementRotation(element2));
            clash.TypeOfClash = element1.Category?.ToString();
            clash.OverlapVolume = diffSolid.Volume;
            return clash;
        }

        // Best-effort orientation of an element, for reporting only. 0 when it has no Location.
        private double GetElementRotation(Element element)
        {
            Location location = element.Location;
            if (location is LocationPoint locationPoint)
            {
                return locationPoint.Rotation;
            }
            if (location is LocationCurve && GetElementCurve(element) is Line line)
            {
                return Math.Acos(line.Direction.DotProduct(XYZ.BasisY));
            }
            return 0;
        }

        private Solid TryGetSolid(Element element, Document doc)
        {
            try
            {
                return GetElementSolid(element, geomOptions);
            }
            catch (Exception)
            {
                _solidErrors?.Add($"{doc?.Title}: could not build solid for element {GetIdValue(element.Id)}");
                return null;
            }
        }

        // World-aligned bounding box: transforms all 8 corners so a rotated link transform
        // still yields a valid (min <= max) axis-aligned box for the broad-phase test.
        private BoundingBoxXYZ TransformBbox(BoundingBoxXYZ box, Transform transform)
        {
            XYZ[] corners =
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Max.Z),
            };
            XYZ min = null;
            XYZ max = null;
            foreach (XYZ corner in corners)
            {
                XYZ p = transform.OfPoint(corner);
                if (min == null)
                {
                    min = p;
                    max = p;
                    continue;
                }
                min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            return new BoundingBoxXYZ { Min = min, Max = max };
        }

        private void PopulateSettings()
        {
            foreach (string title in DocumentMap.Keys)
            {
                Settings.RevitModels1.Add(new ListItem(title));
                Settings.RevitModels2.Add(new ListItem(title));
            }
        }
        public void PopulateLinkedModels()
        {
            DocumentMap = new Dictionary<string, Document>();
            RevitLinkInstanceMap = new Dictionary<string, RevitLinkInstance>();
            //Add host model to dictionary first
            DocumentMap.Add(doc.Title, doc);

            //then add all the linked models
            IList<Element> linkedInstances = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).ToElements();
            foreach (Element linkElement in linkedInstances)
            {
                RevitLinkInstance linkedInstance = linkElement as RevitLinkInstance;
                if (RevitLinkType.IsLoaded(doc, linkedInstance.GetTypeId()))
                {
                    Document linkdoc = linkedInstance.GetLinkDocument();
                    DocumentMap.Add(linkdoc.Title, linkdoc);
                    RevitLinkInstanceMap.Add(linkdoc.Title, linkedInstance);
                }
            }
        }

        public Curve GetElementCurve(Element element)
        {
            if (element.Location is LocationCurve)
            {
                LocationCurve curve = element.Location as LocationCurve;
                return curve.Curve;
            }
            return null;
        }

        public bool BboxIntersects(BoundingBoxXYZ box1, BoundingBoxXYZ box2)
        {
            XYZ min1 = box1.Min;
            XYZ max1 = box1.Max;
            XYZ min2 = box2.Min;
            XYZ max2 = box2.Max;
            bool result = (min1.X <= max2.X && max1.X >= min2.X) && (min1.Y <= max2.Y && max1.Y >= min2.Y) && (min1.Z <= max2.Z && max1.Z >= min2.Z);
            return result;
        }

        public Solid GetElementSolid(Element element, Options geomOptions)
        {
            GeometryElement geomElement = element.get_Geometry(geomOptions);
            List<Solid> solids = new List<Solid>();
            Solid union = null;
            foreach (GeometryObject geomobject in geomElement)
            {
                Solid solid = geomobject as Solid;
                if (solid != null && solid?.Volume > 0)
                {
                    solids.Add(solid);
                }
                GeometryInstance geomInst = geomobject as GeometryInstance;
                if (null == geomInst)
                {
                    continue;
                }
                foreach (GeometryObject geom in geomInst.GetInstanceGeometry())
                {
                    Solid subsolid = geom as Solid;
                    if (subsolid != null || subsolid?.Volume > 0)
                    {
                        solids.Add(subsolid);
                    }
                }
            }
            List<Solid> failedSolids = new List<Solid>();
            foreach (Solid subsolid in solids)
            {
                if (null == union)
                {
                    union = subsolid;
                }
                else
                {
                    try
                    {
                        union = BooleanOperationsUtils.ExecuteBooleanOperation(union, subsolid, BooleanOperationsType.Union);
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        failedSolids.Add(subsolid);
                        continue;
                    }
                }
            }
            if (union != null && failedSolids.Any())
            {
                foreach (Solid failedSolid in failedSolids)
                {
                    try
                    {
                        union = BooleanOperationsUtils.ExecuteBooleanOperation(union, failedSolid, BooleanOperationsType.Union);
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        throw new Exception("Could not create Solid from Element");
                    }
                }
            }

            return union;
        }

        public Solid SolidByBoundingBox(BoundingBoxXYZ bbox)
        {
            // corners in BBox coords
            XYZ pt0 = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
            XYZ pt1 = new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z);
            XYZ pt2 = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z);
            XYZ pt3 = new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z);
            //edges in BBox coords
            Line edge0 = Line.CreateBound(pt0, pt1);
            Line edge1 = Line.CreateBound(pt1, pt2);
            Line edge2 = Line.CreateBound(pt2, pt3);
            Line edge3 = Line.CreateBound(pt3, pt0);
            //create loop, still in BBox coords
            List<Curve> edges = new List<Curve>();
            edges.Add(edge0);
            edges.Add(edge1);
            edges.Add(edge2);
            edges.Add(edge3);
            Double height = bbox.Max.Z - bbox.Min.Z;
            CurveLoop baseLoop = CurveLoop.Create(edges);
            List<CurveLoop> loopList = new List<CurveLoop>();
            loopList.Add(baseLoop);
            Solid transformBox = GeometryCreationUtilities.CreateExtrusionGeometry(loopList, XYZ.BasisZ, height);

            return transformBox;

        }

        public Task<IReadOnlyList<ClashDto>> RunClashesAsync()
        {
            _runTcs = new TaskCompletionSource<IReadOnlyList<ClashDto>>();
            MakeRequest(RequestId.RunRevitClashes);
            return _runTcs.Task;
        }

        internal void ExecuteClashRun()
        {
            try
            {
                List<Clash> clashes = RunClashes();
                IReadOnlyList<ClashDto> dtos = clashes.Select(MapToDto).ToList();
                if (_solidErrors != null && _solidErrors.Count > 0)
                {
                    TaskDialog.Show("Clash Detector",
                        $"{_solidErrors.Count} element(s) could not be processed and were approximated by their bounding box or skipped.");
                }
                _runTcs?.TrySetResult(dtos);
            }
            catch (Exception ex)
            {
                _runTcs?.TrySetException(ex);
            }
        }

        // ft³ -> cm³
        private const double CubicFeetToCubicCentimetres = 28316.846592;

        private ClashDto MapToDto(Clash clash)
        {
            string openTitle = doc?.Title;
            return new ClashDto
            {
                ElementId1 = GetIdValue(clash.Element1.Id),
                ElementId2 = GetIdValue(clash.Element2.Id),
                Document1 = clash.Document1?.Title,
                Document2 = clash.Document2?.Title,
                ElementName1 = clash.Element1?.Name,
                ElementName2 = clash.Element2?.Name,
                Category1 = clash.Element1?.Category?.Name,
                Category2 = clash.Element2?.Category?.Name,
                Level1 = GetLevelName(clash.Document1, clash.Element1),
                Level2 = GetLevelName(clash.Document2, clash.Element2),
                IsInOpenModel1 = openTitle != null && clash.Document1?.Title == openTitle,
                IsInOpenModel2 = openTitle != null && clash.Document2?.Title == openTitle,
                TypeOfClash = clash.TypeOfClash,
                OverlapVolume = clash.OverlapVolume * CubicFeetToCubicCentimetres,
                X = clash.RevitPoint?.X ?? 0,
                Y = clash.RevitPoint?.Y ?? 0,
                Z = clash.RevitPoint?.Z ?? 0,
                Rotation = clash.Rotation,
            };
        }

        private static string GetLevelName(Document document, Element element)
        {
            if (document == null || element == null)
            {
                return null;
            }

            ElementId levelId = element.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                Level direct = document.GetElement(levelId) as Level;
                if (direct != null)
                {
                    return direct.Name;
                }
            }

            BuiltInParameter[] levelParams =
            {
                BuiltInParameter.RBS_START_LEVEL_PARAM,
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
            };
            foreach (BuiltInParameter bip in levelParams)
            {
                Parameter p = element.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        Level lvl = document.GetElement(id) as Level;
                        if (lvl != null)
                        {
                            return lvl.Name;
                        }
                    }
                }
            }
            return null;
        }

        private static long GetIdValue(ElementId id)
        {
#if REVIT2025
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private static ElementId MakeElementId(long value)
        {
#if REVIT2025
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        public void MakeRequest(RequestId request)
        {
            handler.Request.Make(request);
            exEvent.Raise();
        }

        private List<long> _pendingSelectionIds;

        public void SelectInOpenModel(IEnumerable<long> elementIds)
        {
            _pendingSelectionIds = elementIds?.ToList() ?? new List<long>();
            MakeRequest(RequestId.SelectElements);
        }

        public void ZoomTo(IEnumerable<long> elementIds)
        {
            _pendingSelectionIds = elementIds?.ToList() ?? new List<long>();
            MakeRequest(RequestId.ZoomElements);
        }

        internal void ExecuteSelectInOpenModel()
        {
            UIDocument uidoc = UIApp.ActiveUIDocument;
            if (uidoc == null || _pendingSelectionIds == null)
            {
                return;
            }
            ICollection<ElementId> ids = _pendingSelectionIds.Select(MakeElementId).ToList();
            uidoc.Selection.SetElementIds(ids);
        }

        internal void ExecuteZoomTo()
        {
            UIDocument uidoc = UIApp.ActiveUIDocument;
            if (uidoc == null || _pendingSelectionIds == null || _pendingSelectionIds.Count == 0)
            {
                return;
            }
            ICollection<ElementId> ids = _pendingSelectionIds.Select(MakeElementId).ToList();
            uidoc.ShowElements(ids);
        }
    }
}
