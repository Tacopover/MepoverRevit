using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OffsetIncrementer.ViewModels;
using OffsetIncrementer.Views;

namespace OffsetIncrementer
{
    /// <summary>Modal dialog that increments the offset parameter of the selected elements.</summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class OffsetIncrementerCommand : IExternalCommand
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
                var vm = new OffsetIncrementerViewModel(uidoc, selectedIds);
                var window = new OffsetIncrementerWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Offset Incrementer", ex.Message);
                return Result.Failed;
            }
        }
    }
}
