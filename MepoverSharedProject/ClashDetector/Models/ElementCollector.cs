using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClashDetector.Models
{

    public class ElementCollector
    {
        private readonly UIDocument _uiDocument;
        private readonly Application _app;
        private readonly Document _parentDocument;
        private readonly View _activeView;
        public Dictionary<string, List<Element>> ModelElementMap;

        /// <summary>
        /// A class for collecting elements from parent and linked models.
        /// </summary>
        /// <param name="uidoc">The UIDocument instance to operate within.</param>
        public ElementCollector(UIDocument uidoc)
        {
            _uiDocument = uidoc;
            _app = uidoc.Application.Application;
            _parentDocument = uidoc.Document;
            _activeView = _parentDocument.ActiveView;
        }

        public Dictionary<string, List<Element>> GetVisibleElements()
        {
            VisibleElementContextBase context = null;
            ViewType viewtype = _activeView.ViewType;

            if (viewtype == ViewType.ThreeD)
            {
                context = new VisibleElementContext(_parentDocument);
            }
            else if (viewtype == ViewType.FloorPlan ||
                    viewtype == ViewType.Section ||
                    viewtype == ViewType.Elevation ||
                    viewtype == ViewType.CeilingPlan ||
                    viewtype == ViewType.AreaPlan ||
                    viewtype == ViewType.EngineeringPlan)
            {
                context = new VisibleElementContext2D(_parentDocument);
            }

            if (context == null)
            {
                TaskDialog.Show("Error", "Unsupported view type.");
                return new Dictionary<string, List<Element>>();
            }

            CustomExporter exp = null;
            if (context is VisibleElementContext visibleElementContext)
            {
                exp = new CustomExporter(_parentDocument, visibleElementContext);
            }
            else if (context is VisibleElementContext2D visibleElementContext2D)
            {
                exp = new CustomExporter(_parentDocument, visibleElementContext2D);
            }

            if (exp == null)
            {
                TaskDialog.Show("Error", "Unsupported context type.");
                return new Dictionary<string, List<Element>>();
            }

            List<ElementId> views = new List<ElementId>
        {
            _activeView.Id
        };
            exp.Export(views);
            ModelElementMap = context.DocMap;
            //List<Element> elements = context.Elements.Where(e => e != null && e.GetType() != typeof(Element) && e.GetType() != typeof(Group)).ToList();
            //List<ElementId> uniqIds = context.Elements.Where(e => e != null).Select(e => e.Id).Distinct().ToList();

            Dictionary<string, List<Element>> purgedMap = new Dictionary<string, List<Element>>();
            // Apply LINQ queries on ModelElementMap values
            foreach (var keyValuePair in ModelElementMap)
            {
                List<Element> value = keyValuePair.Value;
                List<Element> uniqList = value
                    .GroupBy(e => e.Id).Select(g => g.First()).ToList();

                List<Element> purgedList = uniqList
                    .Where(e => e != null && e.GetType() != typeof(Element) &&
                    e.GetType() != typeof(Group) && e.GetType() != typeof(AssemblyInstance))
                    .ToList();

                purgedMap[keyValuePair.Key] = purgedList;
            }

            return purgedMap;
        }

    }

    internal abstract class VisibleElementContextBase
    {
        public Document ParentDocument { get; protected set; }
        public Document CurrentDocument { get; protected set; }
        public List<Document> Documents { get; protected set; }
        public List<Element> Elements { get; protected set; }
        public List<ElementId> ElementIds { get; protected set; }
        public Dictionary<string, List<Element>> DocMap { get; protected set; }

        protected VisibleElementContextBase(Document doc)
        {
            ParentDocument = doc;
            CurrentDocument = doc;
            Documents = new List<Document> { doc };
            DocMap = new Dictionary<string, List<Element>>() { { doc.Title, new List<Element>() } };
            Elements = new List<Element>();
            ElementIds = new List<ElementId>();
        }

        public bool Start()
        {
            return true;
        }

        public void Finish()
        {
        }

        public bool IsCanceled()
        {
            return false;
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            Document doc = node.GetDocument();
            Documents.Add(doc);
            CurrentDocument = doc;
            if (DocMap.ContainsKey(doc.Title) == false)
            {
                DocMap.Add(doc.Title, new List<Element>());
            }

            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
            CurrentDocument = ParentDocument;
        }

        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public void OnViewEnd(ElementId elementId)
        {

        }

        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
        }
        public void OnRPC(RPCNode node)
        {
        }
        public void OnLight(LightNode node)
        {
        }
        public RenderNodeAction OnFaceBegin(FaceNode node)
        {
            return RenderNodeAction.Skip;
        }
        public void OnFaceEnd(FaceNode node)
        {
        }
        public void OnPolymesh(PolymeshTopology node)
        {
        }
        public void OnMaterial(MaterialNode node)
        {
        }
    }


    internal class VisibleElementContext : VisibleElementContextBase, IExportContext
    {

        internal VisibleElementContext(Document doc) : base(doc)
        {
            ParentDocument = doc;
            CurrentDocument = doc;
            Documents = new List<Document> { doc };
            DocMap = new Dictionary<string, List<Element>>() { { doc.Title, new List<Element>() } };
            Elements = new List<Element>();
            ElementIds = new List<ElementId>();
        }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            ElementIds.Add(elementId);
            Element elem = CurrentDocument.GetElement(elementId);
            Elements.Add(elem);

            if (DocMap.TryGetValue(CurrentDocument.Title, out List<Element> elems))
            {
                elems.Add(elem);
            }

            return RenderNodeAction.Proceed;
        }

        public void OnElementEnd(ElementId elementId)
        {
        }
    }

    internal class VisibleElementContext2D : VisibleElementContextBase, IExportContext2D
    {
        internal VisibleElementContext2D(Document doc) : base(doc)
        {
            ParentDocument = doc;
            CurrentDocument = doc;
            Documents = new List<Document> { doc };
            DocMap = new Dictionary<string, List<Element>>() { { doc.Title, new List<Element>() } };
            Elements = new List<Element>();
            ElementIds = new List<ElementId>();
        }

        public RenderNodeAction OnElementBegin2D(ElementNode node)
        {
            ElementIds.Add(node.ElementId);
            Element elem = CurrentDocument.GetElement(node.ElementId);
            Elements.Add(elem);

            if (DocMap.TryGetValue(CurrentDocument.Title, out List<Element> elems))
            {
                elems.Add(elem);
            }

            return RenderNodeAction.Proceed;
        }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            return RenderNodeAction.Skip;
        }

        public void OnElementEnd(ElementId elementId)
        {

        }

        public void OnElementEnd2D(ElementNode node)
        {

        }


        public RenderNodeAction OnCurve(CurveNode node)
        {
            return RenderNodeAction.Skip;
        }
        public RenderNodeAction OnFaceEdge2D(FaceEdgeNode node)
        {
            return RenderNodeAction.Skip;
        }

        public RenderNodeAction OnFaceSilhouette2D(FaceSilhouetteNode node)
        {
            return RenderNodeAction.Skip;
        }

        public void OnLineSegment(LineSegment segment)
        {

        }

        public RenderNodeAction OnPolyline(PolylineNode node)
        {
            return RenderNodeAction.Skip;
        }

        public void OnPolylineSegments(PolylineSegments segments)
        {

        }

        public void OnText(TextNode node)
        {

        }

    }
}
