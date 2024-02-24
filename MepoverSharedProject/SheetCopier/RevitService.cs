using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MepoverSharedProject.SheetCopier
{
    public class RevitService
    {
        private UIApplication uiApp;
        private Autodesk.Revit.ApplicationServices.Application m_app;
        private Document m_doc;
        private Dictionary<string, bool> annotationChecks;
        private List<Level> levelsHost;
        private List<double> levelsElevations;

        #region properties
        private List<Document> _openDocuments;
        public List<Document> OpenDocuments
        {
            get
            {
                if (_openDocuments == null)
                {
                    _openDocuments = new List<Document>();
                }
                return _openDocuments;
            }
            set { _openDocuments = value; }
        }


        #endregion

        public RevitService(UIApplication uiapp)
        {
            uiApp = uiapp;
            m_app = uiapp.Application;
            m_doc = uiApp.ActiveUIDocument.Document;
            DocumentSet docSet = uiApp.Application.Documents;
            foreach (Document doc in docSet)
            {
                string docName = doc.Title;
                if (docName == m_doc.Title)
                {
                    continue;
                }
                OpenDocuments.Add(doc);
            }
            levelsHost = new FilteredElementCollector(m_doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            levelsElevations = levelsHost.Select(l => Math.Round(l.Elevation, 2)).ToList();
        }

        public void CopySheets(Document linkdoc, List<ViewSheet> sheets, Dictionary<string, bool> annotationChecks)
        {
            this.annotationChecks = annotationChecks;
            List<ViewSheet> hostSheets = new FilteredElementCollector(m_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            List<string> sheetNumbers = hostSheets.Select(s => s.SheetNumber).ToList();
            List<string> failedSheetNumbers = new List<string>();

            List<Element> titleBlockTypesHost = new FilteredElementCollector(m_doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().ToElements() as List<Element>;
            Dictionary<string, FamilySymbol> tbTypesDict = new Dictionary<string, FamilySymbol>();
            foreach (Element tbTypeElement in titleBlockTypesHost)
            {
                FamilySymbol tbType = tbTypeElement as FamilySymbol;
                tbTypesDict[tbType.FamilyName + " : " + tbType.Name] = tbType;
            }

            CopyPasteOptions pasteOptions = new CopyPasteOptions();
            pasteOptions.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

            using (Transaction copyTransaction = new Transaction(m_doc))
            {
                copyTransaction.Start("Copy sheets");

                Dictionary<ElementId, ElementId> potentialParentViewIds = new Dictionary<ElementId, ElementId>();
                foreach (ViewSheet sheet in sheets)
                {
                    List<ElementId> viewportLinkIds = sheet.GetAllViewports() as List<ElementId>;
                    if (viewportLinkIds == null || viewportLinkIds.Count == 0)
                    {
                        continue;
                    }
                    foreach (ElementId id in viewportLinkIds)
                    {
                        Viewport viewportLink = linkdoc.GetElement(id) as Viewport;
                        View viewLink = linkdoc.GetElement(viewportLink.ViewId) as View;

#if !REVIT2021
                        
                        if (viewLink.IsCallout)
                        {
                            potentialParentViewIds[viewLink.GetCalloutParentId()] = ElementId.InvalidElementId;
                        }
#endif
                    }
                }

                foreach (ViewSheet sheet in sheets)
                {
                    // get title block from link
                    ElementId titleBlockLinkId = new FilteredElementCollector(linkdoc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElementId();
                    FamilyInstance titleBlockLink = linkdoc.GetElement(titleBlockLinkId) as FamilyInstance;
                    FamilySymbol titleBlockLinkSymbol = titleBlockLink.Symbol;
                    string tbFamNameLink = titleBlockLinkSymbol.FamilyName;
                    string tbTypeNameLink = titleBlockLinkSymbol.Name;

                    FamilySymbol titleBlock;
                    tbTypesDict.TryGetValue(tbFamNameLink + " : " + tbTypeNameLink, out titleBlock);
                    //check if title block exists in hosts
                    if (titleBlock == null)
                    {
                        titleBlock = new FilteredElementCollector(m_doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElement() as FamilySymbol;
                    }
                    // create new sheet, copy parameter values
                    ViewSheet newSheet = ViewSheet.Create(m_doc, titleBlock.Id);

                    List<ElementId> viewportLinkIds = sheet.GetAllViewports() as List<ElementId>;
                    if (viewportLinkIds == null || viewportLinkIds.Count == 0)
                    {
                        continue;
                    }
                    List<Element> viewportsLink = new List<Element>();
                    List<XYZ> viewportLocationsLink = new List<XYZ>();
                    List<ElementId> viewIdsLink = new List<ElementId>();
                    List<ViewportRotation> viewportRotationsLink = new List<ViewportRotation>();
                    foreach (ElementId id in viewportLinkIds)
                    {
                        Viewport viewportLink = linkdoc.GetElement(id) as Viewport;
                        viewportRotationsLink.Add(viewportLink.Rotation);
                        viewportLocationsLink.Add(viewportLink.GetBoxCenter());
                        viewIdsLink.Add(viewportLink.ViewId);
                        viewportsLink.Add(viewportLink);
                    }
                    // check if a there is a dependent view among the views. If there is: get its primary view, copy that view, create a new dependent from that
                    // and then set the crop box/scope box to be the same as the original dependent view

                    m_doc.Regenerate();
                    for (int i = 0; i < viewIdsLink.Count; i++)
                    {
                        List<ElementId> elementId = new List<ElementId> { viewIdsLink[i] };
                        View viewLink = linkdoc.GetElement(viewIdsLink[i]) as View;

                        // check if the view's level's elevation is in the list of host levels' elevations
                        if (viewLink.GenLevel != null)
                        {
                            if (!levelsElevations.Contains(Math.Round(viewLink.GenLevel.Elevation, 2)))
                            {
                                TaskDialog.Show("error", "Could not copy views from linked sheet because a view's level elevation " +
                                    "could not be found in the document you want to paste to. Please make sure the level elevations in both " +
                                    "documents align");
                                continue;
                            }

                        }

                        if (viewLink.ViewType == ViewType.CeilingPlan)
                        {
                            failedSheetNumbers.Add("Ceiling plan not supported, failed to copy:\n\t" + sheet.SheetNumber + " - " + viewLink.Name);
                            continue;
                        }
                        List<ElementId> copiedViewId;
#if !REVIT2021
                        if (viewLink.IsCallout)
                        {
                            ElementId parentViewId = viewLink.GetCalloutParentId();
                            View parentViewLink = linkdoc.GetElement(parentViewId) as View;
                            if (parentViewLink == null)
                            {
                                failedSheetNumbers.Add("Could not get callout parent view, failed to copy:\n\t"+sheet.SheetNumber + " - " + viewLink.Name);
                                continue;
                            }
                            ElementId parentViewIdHost;
                            potentialParentViewIds.TryGetValue(parentViewId, out parentViewIdHost);
                            if (parentViewIdHost == ElementId.InvalidElementId)
                            {
                                parentViewIdHost = (ElementTransformUtils.CopyElements(linkdoc, new List<ElementId> { parentViewId }, m_doc, null, pasteOptions) as List<ElementId>)[0];
                            }

                            View parentViewHost = m_doc.GetElement(parentViewIdHost) as View;
                            copiedViewId = ElementTransformUtils.CopyElements(parentViewLink, new List<ElementId> { viewLink.Id }, parentViewHost, null, pasteOptions) as List<ElementId>;

                        }
#endif
                        // if the view has a primary view then it is a dependent view                        
                        if (viewLink.GetPrimaryViewId() != ElementId.InvalidElementId)
                        {
                            failedSheetNumbers.Add("Dependent views not supported, failed to copy:\n\t" + sheet.SheetNumber + " - " + viewLink.Name);
                            continue;
                            //ElementId primaryViewId = viewLink.GetPrimaryViewId();
                            //View primaryViewLink = linkdoc.GetElement(primaryViewId) as View;
                            //copiedViewId = ElementTransformUtils.CopyElements(linkdoc, new List<ElementId> { primaryViewId }, m_doc, null, pasteOptions) as List<ElementId>;
                            //View primaryViewHost = m_doc.GetElement(copiedViewId[0]) as View;
                            ////  create a new duplicate as dependent view and set the crop box to be the same as the original dependent view
                            //ElementId dependentViewHostId = primaryViewHost.Duplicate(ViewDuplicateOption.AsDependent);
                            //View dependentViewHost = m_doc.GetElement(dependentViewHostId) as View;
                            //dependentViewHost.CropBox = viewLink.CropBox;

                        }
                        else
                        {
#if REVIT2021
                            //if a view is a callout in Revit 2021, it is not possible to copy it directly and no way to check if it is a callout, so we continue
                            try
                            {
                                copiedViewId = ElementTransformUtils.CopyElements(linkdoc, elementId, m_doc, null, pasteOptions) as List<ElementId>;
                            }
                            catch (Exception)
                            {
                                failedSheetNumbers.Add("Callout views not supported, failed to copy:\n\t" + sheet.SheetNumber + " - " + viewLink.Name);
                                continue;
                            }
#else
                            copiedViewId = ElementTransformUtils.CopyElements(linkdoc, elementId, m_doc, null, pasteOptions) as List<ElementId>;
#endif
                        }

                        View viewHost = m_doc.GetElement(copiedViewId[0]) as View;


                        List<ElementId> annotationsIds = GetViewAnnotations(viewLink, linkdoc);
                        if (annotationsIds.Count > 0)
                        {
                            List<ElementId> copiedAnnotationIds = ElementTransformUtils.CopyElements(viewLink, annotationsIds, viewHost, null, pasteOptions) as List<ElementId>;
                        }


                        Viewport viewportHost = Viewport.Create(m_doc, newSheet.Id, copiedViewId[0], XYZ.Zero);
                        if (viewportHost == null)
                        {
                            failedSheetNumbers.Add("Failed to create viewport for:\n\t" + sheet.SheetNumber + " - " + viewHost.Name);
                            continue;
                        }
                        viewportHost.Rotation = viewportRotationsLink[i];
                        viewportHost.SetBoxCenter(viewportLocationsLink[i]);
                    }

                    //get the schedules separately because they are not viewports
                    List<Element> sheetElements = new FilteredElementCollector(linkdoc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).ToElements().ToList();
                    foreach (Element element in sheetElements)
                    {
                        ScheduleSheetInstance schedule = element as ScheduleSheetInstance;
                        if (schedule.Name.Contains("<Revision Schedule>")) continue;

                        XYZ schedulePoint = schedule.Point;
                        ElementId scheduleId = schedule.ScheduleId;
                        List<ElementId> scheduleIds = new List<ElementId> { scheduleId };
                        List<ElementId> copiedScheduleIds = ElementTransformUtils.CopyElements(linkdoc, scheduleIds, m_doc, null, pasteOptions) as List<ElementId>;
                        ScheduleSheetInstance scheduleHost = ScheduleSheetInstance.Create(m_doc, newSheet.Id, copiedScheduleIds[0], schedulePoint);
                    }

                    m_doc.Regenerate();

                    newSheet.Name = sheet.Name;
                    string sheetNumber = sheet.SheetNumber;

                    while (sheetNumbers.Contains(sheetNumber))
                    {
                        sheetNumber = sheetNumber + "_copy";
                    }

                    newSheet.SheetNumber = sheetNumber;
                }


                copyTransaction.Commit();

                if (failedSheetNumbers.Count > 0)
                {
                    string message = "The following sheets contained viewports that could not be copied: \n";
                    int i = 1;
                    foreach (string sheetNumber in failedSheetNumbers)
                    {
                        message += i.ToString() + ": " + sheetNumber + "\n";
                    }
                    TaskDialog.Show("Failed to copy sheets", message);
                }
            }



        }

        private List<ElementId> GetViewAnnotations(View view, Document linkdoc)
        {
            List<ElementId> annotations = new List<ElementId>();
            if (annotationChecks["CopyAnnotations"])
            {
                FilteredElementCollector annotationSymbolCollector = new FilteredElementCollector(linkdoc, view.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation);
                List<ElementId> annotationSymbols = annotationSymbolCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(annotationSymbols);
            }
            if (annotationChecks["CopyDetailItems"])
            {
                FilteredElementCollector detailItemCollector = new FilteredElementCollector(linkdoc, view.Id).OfCategory(BuiltInCategory.OST_DetailComponents);
                List<ElementId> detailItems = detailItemCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(detailItems);
            }
            if (annotationChecks["CopyRegions"])
            {
                FilteredElementCollector filledRegionCollector = new FilteredElementCollector(linkdoc, view.Id).OfClass(typeof(FilledRegion));
                List<ElementId> filledRegions = filledRegionCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(filledRegions);
            }
            if (annotationChecks["CopyDimensions"])
            {
                FilteredElementCollector dimensionCollector = new FilteredElementCollector(linkdoc, view.Id).OfClass(typeof(Dimension));
                List<ElementId> dimensions = dimensionCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(dimensions);
            }
            if (annotationChecks["CopyTextNotes"])
            {
                FilteredElementCollector textNoteCollector = new FilteredElementCollector(linkdoc, view.Id).OfClass(typeof(TextNote));
                List<ElementId> textNotes = textNoteCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(textNotes);
            }
            if (annotationChecks["CopyLines"])
            {
                FilteredElementCollector lineCollector = new FilteredElementCollector(linkdoc, view.Id).OfCategory(BuiltInCategory.OST_Lines);
                List<ElementId> lines = lineCollector.ToElementIds() as List<ElementId>;
                annotations.AddRange(lines);
            }

            return annotations;
        }
        private List<Document> GetLinkDocuments()
        {
            FilteredElementCollector collector = new FilteredElementCollector(m_doc);
            collector.OfClass(typeof(RevitLinkInstance));
            List<Document> linkDocuments = new List<Document>();
            foreach (RevitLinkInstance linkInstance in collector)
            {
                ElementId typeId = linkInstance.GetTypeId();
                if (typeId == null) { continue; }
                Document linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) { continue; }

                RevitLinkType linkType = m_doc.GetElement(typeId) as RevitLinkType;
                if (linkType.IsNestedLink) { continue; }
                if (!(linkType.GetLinkedFileStatus() == LinkedFileStatus.Loaded)) { continue; }

                linkDocuments.Add(linkInstance.GetLinkDocument());

            }
            return linkDocuments;
        }

        public List<Element> GetSheets(Document linkdoc)
        {
            FilteredElementCollector sheetCollector = new FilteredElementCollector(linkdoc).OfClass(typeof(ViewSheet));
            List<Element> sheets = (List<Element>)sheetCollector.ToElements();
            return sheets;
        }

        public class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }
    }

}
