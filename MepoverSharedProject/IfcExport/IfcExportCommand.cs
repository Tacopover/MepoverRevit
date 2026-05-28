using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
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
        internal static IfcExportViewModel _viewModel;
        internal static IfcExportRequestHandler _handler;
        internal static ExternalEvent _externalEvent;
        internal static IfcExportPersistenceService _persistence;

        /// <summary>
        /// Called from RevitApplication.OnStartup to create the handler/event in a valid API
        /// context and subscribe the DocumentOpened auto-start hook.
        /// </summary>
        internal static void InitializeForAutoStart(
            UIControlledApplication app,
            IfcExportPersistenceService persistence)
        {
            _persistence = persistence;
            _handler = new IfcExportRequestHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            app.ControlledApplication.DocumentOpened += OnDocumentOpenedForAutoStart;
        }

        private static void OnDocumentOpenedForAutoStart(object sender, DocumentOpenedEventArgs e)
        {
            string title = e.Document?.Title;
            if (string.IsNullOrEmpty(title)) return;
            if (_persistence == null || !_persistence.TryGetSettings(title, out var settings)) return;

            // Don't interrupt an export that's already running for another document.
            var orchState = _handler?.Orchestrator?.State;
            if (orchState == ExportState.Running || orchState == ExportState.Paused) return;

            var uiApp = new UIApplication((Autodesk.Revit.ApplicationServices.Application)sender);
            _viewModel = new IfcExportViewModel(uiApp, _handler, _externalEvent, _persistence);
            _viewModel.ApplySettings(settings);
            _viewModel.TriggerStart();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;

                if (_viewModel == null)
                {
                    // First time — create everything. Handler/event may already exist from
                    // InitializeForAutoStart; create them here as fallback if not.
                    if (_handler == null)
                    {
                        _handler = new IfcExportRequestHandler();
                        _externalEvent = ExternalEvent.Create(_handler);
                    }
                    _viewModel = new IfcExportViewModel(uiApp, _handler, _externalEvent, _persistence);
                }

                // ShowWindow handles both "create and show" and "activate existing" internally.
                _viewModel.ShowWindow();

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
