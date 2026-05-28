using Autodesk.Revit.DB;
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
        QuickExport,
        ExportActiveView
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

        // Set before raising an ExportActiveView request.
        public Action<string> ExportViewCallback { get; set; }

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
                    QuickExportCallback = null;
                    return;
                }

                if (req == IfcExportRequest.ExportActiveView)
                {
                    ElementId viewId = app.ActiveUIDocument?.ActiveView?.Id;
                    Document doc     = app.ActiveUIDocument?.Document;
                    if (viewId == null || doc == null)
                    {
                        ExportViewCallback?.Invoke("Error: No active view.");
                        ExportViewCallback = null;
                        return;
                    }
                    string result = Orchestrator?.ExportActiveView(viewId, doc)
                                    ?? "Error: Export session not initialised.";
                    ExportViewCallback?.Invoke(result);
                    ExportViewCallback = null;
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
                if (req == IfcExportRequest.QuickExport)
                {
                    QuickExportCallback?.Invoke("Error: " + ex.Message);
                    QuickExportCallback = null;
                }
                else if (req == IfcExportRequest.ExportActiveView)
                {
                    ExportViewCallback?.Invoke("Error: " + ex.Message);
                    ExportViewCallback = null;
                }
            }
        }
    }
}
