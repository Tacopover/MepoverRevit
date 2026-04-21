using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace IfcExport
{
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class IfcExportCommand : IExternalCommand
    {
        // Kept as static fields so re-clicking the ribbon button focuses the existing window
        // rather than creating a second one — same pattern as SheetCopier.
        private static IfcExportViewModel _viewModel;
        private static IfcExportRequestHandler _handler;
        private static ExternalEvent _externalEvent;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;

                if (_viewModel == null || _viewModel.IsWindowClosed)
                {
                    // ExternalEvent must be created inside a valid Revit API context (IExternalCommand.Execute).
                    if (_handler == null)
                    {
                        _handler = new IfcExportRequestHandler();
                        _externalEvent = ExternalEvent.Create(_handler);
                    }

                    _viewModel = new IfcExportViewModel(uiApp, _handler, _externalEvent);
                    _viewModel.ShowWindow();
                }
                else
                {
                    _viewModel.Window.Activate();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.GetType().Name + ": " + ex.Message,
                    "IFC Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}
