using Autodesk.Revit.UI;
using Autodesk.Revit.UI;
using System;

namespace IfcExport
{
    public enum IfcExportRequest
    {
        None,
        Start,
        Pause,
        Resume,
        Cancel,
        QuickExport
    }

    /// <summary>
    /// Bridges WPF button clicks into a valid Revit API context via ExternalEvent.
    /// All orchestrator calls that touch the Revit API (e.g. subscribing to Idling)
    /// must go through here.
    /// </summary>
    public class IfcExportRequestHandler : IExternalEventHandler
    {
        private volatile IfcExportRequest _pendingRequest = IfcExportRequest.None;

        // Set by the ViewModel once the orchestrator is created.
        public IdleExportOrchestrator Orchestrator { get; set; }

        // Set before raising a QuickExport request.
        public string QuickExportDestination { get; set; }
        public Action<string> QuickExportCallback { get; set; }

        public void Request(IfcExportRequest request)
        {
            _pendingRequest = request;
        }

        public string GetName() => "IfcExport";

        public void Execute(UIApplication app)
        {
            IfcExportRequest req = _pendingRequest;
            _pendingRequest = IfcExportRequest.None;

            try
            {
                if (req == IfcExportRequest.QuickExport)
                {
                    string result = IfcQuickExporter.Export(app, QuickExportDestination);
                    QuickExportCallback?.Invoke(result);
                    return;
                }

                if (Orchestrator == null)
                    return;

                switch (req)
                {
                    case IfcExportRequest.Start:
                        Orchestrator.Start();
                        break;
                    case IfcExportRequest.Pause:
                        Orchestrator.Pause();
                        break;
                    case IfcExportRequest.Resume:
                        Orchestrator.Resume();
                        break;
                    case IfcExportRequest.Cancel:
                        Orchestrator.Cancel();
                        break;
                }
            }
            catch (Exception ex)
            {
                Orchestrator?.ReportError("Request handler error: " + ex.Message);
                QuickExportCallback?.Invoke("Error: " + ex.Message);
            }
        }
    }
}
