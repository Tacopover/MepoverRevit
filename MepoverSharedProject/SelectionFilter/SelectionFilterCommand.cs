using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SelectionFilter.ViewModels;
using SelectionFilter.Views;

namespace SelectionFilter
{
    /// <summary>Modal dialog that filters the current selection by a parameter value.</summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class SelectionFilterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds.Count == 0)
                {
                    TaskDialog.Show("Selection Filter", "Select some elements first, then run Selection Filter.");
                    return Result.Cancelled;
                }

                var vm = new SelectionFilterViewModel(uidoc, selectedIds);
                var window = new SelectionFilterWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Selection Filter", ex.Message);
                return Result.Failed;
            }
        }
    }
}
