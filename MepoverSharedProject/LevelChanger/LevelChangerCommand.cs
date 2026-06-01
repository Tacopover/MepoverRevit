using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using LevelChanger.ViewModels;
using LevelChanger.Views;

namespace LevelChanger
{
    /// <summary>Modal dialog that reassigns selected (or all) MEP elements to the closest level.</summary>
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class LevelChangerCommand : IExternalCommand
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
                var vm = new LevelChangerViewModel(uidoc, selectedIds);
                var window = new LevelChangerWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Change Object Levels", ex.Message);
                return Result.Failed;
            }
        }
    }
}
