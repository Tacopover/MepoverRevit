using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClashDetector.ViewModels;
using ClashDetector.Views;
using System;
using System.Windows;
using System.Windows.Interop;

namespace ClashDetector
{
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class ClashDetectorCommand : IExternalCommand
    {
        private static ClashDetectorWindow _window;
        private static RevitClashService _service;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                UIApplication uiApp = commandData.Application;
                _service = new RevitClashService(uiApp);
                _service.Initialize();

                var viewModel = new ClashDetectorViewModel(_service);
                _window = new ClashDetectorWindow { DataContext = viewModel };
                new WindowInteropHelper(_window).Owner = uiApp.MainWindowHandle;
                _window.Closed += (s, e) => _window = null;
                _window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetType().Name + " " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
