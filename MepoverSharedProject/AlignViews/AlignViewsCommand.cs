using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AlignViews
{
    /// <summary>
    /// Copies the zoom/pan rectangle of the active plan view onto every other open plan view,
    /// so panning around one view keeps the others framed on the same area.
    /// </summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class AlignViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;
            IList<UIView> openViews = uidoc.GetOpenUIViews();

            UIView activeUiView = null;
            foreach (UIView uv in openViews)
            {
                if (uv.ViewId == activeView.Id)
                {
                    activeUiView = uv;
                    break;
                }
            }

            if (activeUiView == null)
            {
                TaskDialog.Show("Align Views",
                    "The active view is not an open, zoomable view. Activate the view you want to use as the reference and try again.");
                return Result.Cancelled;
            }

            try
            {
                IList<XYZ> rect = activeUiView.GetZoomCorners();
                if (rect == null || rect.Count < 2)
                {
                    TaskDialog.Show("Align Views", "Could not read the zoom rectangle of the active view.");
                    return Result.Cancelled;
                }

                foreach (UIView view in openViews)
                {
                    if (view.ViewId == activeUiView.ViewId)
                        continue;

                    View target = doc.GetElement(view.ViewId) as View;
                    if (target == null)
                        continue;

                    if (target.ViewType == ViewType.FloorPlan ||
                        target.ViewType == ViewType.CeilingPlan ||
                        target.ViewType == ViewType.AreaPlan ||
                        target.ViewType == ViewType.EngineeringPlan)
                    {
                        view.ZoomAndCenterRectangle(rect[0], rect[1]);
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Align Views", "Failed to align views:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
