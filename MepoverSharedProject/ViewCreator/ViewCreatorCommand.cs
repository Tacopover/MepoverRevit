using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewCreator.ViewModels;
using ViewCreator.Views;

namespace ViewCreator
{
    /// <summary>Modal dialog that bulk-creates plan views from templates x levels.</summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class ViewCreatorCommand : IExternalCommand
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

                var vm = new ViewCreatorViewModel(uidoc);
                var window = new ViewCreatorWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("View Creator", ex.Message);
                return Result.Failed;
            }
        }
    }
}
