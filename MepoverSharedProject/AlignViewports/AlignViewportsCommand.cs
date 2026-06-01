using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AlignViewports.ViewModels;
using AlignViewports.Views;

namespace AlignViewports
{
    /// <summary>Modal dialog that aligns viewports across sheets.</summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class AlignViewportsCommand : IExternalCommand
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

                var vm = new AlignViewportsViewModel(uidoc);
                var window = new AlignViewportsWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Align Viewports", ex.Message);
                return Result.Failed;
            }
        }
    }
}
