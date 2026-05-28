using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System;
using System.IO;

namespace IfcExport
{
    /// <summary>
    /// Temporary test command. Subscribes to DocumentChanged, DocumentSynchronizingWithCentral,
    /// DocumentSynchronizedWithCentral, and DocumentReloadedLatest, then logs every event to a
    /// temp file so we can verify that remote user changes are reported by DocumentChanged after
    /// a Reload Latest or Sync With Central.
    ///
    /// Usage: click once to start monitoring, click again to stop.
    /// Log path is shown in the TaskDialog on start.
    /// </summary>
    [TransactionAttribute(TransactionMode.ReadOnly)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class WsChangeTestCommand : IExternalCommand
    {
        private static bool _monitoring;
        private static UIApplication _uiApp;
        private static string _logPath;
        private static int _docChangedCount;
        private static bool _syncInProgress;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!_monitoring)
                StartMonitoring(commandData.Application);
            else
                StopMonitoring();

            return Result.Succeeded;
        }

        private static void StartMonitoring(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _logPath = Path.Combine(Path.GetTempPath(), "ws_change_test.log");
            _docChangedCount = 0;
            _syncInProgress = false;

            File.WriteAllText(_logPath,
                $"=== WS Change Test started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n" +
                $"Columns: timestamp  event  [+added ~modified -deleted]\r\n\r\n");

            uiApp.Application.DocumentChanged += OnDocumentChanged;
            uiApp.Application.DocumentSynchronizingWithCentral += OnSynchronizing;
            uiApp.Application.DocumentSynchronizedWithCentral += OnSynchronized;
            uiApp.Application.DocumentReloadedLatest += OnReloadedLatest;

            _monitoring = true;

            TaskDialog td = new TaskDialog("WS Change Test — Monitoring Started");
            td.MainContent =
                "Listening for DocumentChanged, DocumentSynchronizingWithCentral,\n" +
                "DocumentSynchronizedWithCentral, and DocumentReloadedLatest.\n\n" +
                "Log file:\n" + _logPath + "\n\n" +
                "Now have another user edit the model and do a Reload Latest or\n" +
                "Synchronize With Central, then click this button again to stop.";
            td.Show();
        }

        private static void StopMonitoring()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
            _uiApp.Application.DocumentSynchronizingWithCentral -= OnSynchronizing;
            _uiApp.Application.DocumentSynchronizedWithCentral -= OnSynchronized;
            _uiApp.Application.DocumentReloadedLatest -= OnReloadedLatest;

            _monitoring = false;

            AppendLog($"\r\n=== Monitoring stopped {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {_docChangedCount} DocumentChanged events ===\r\n");

            TaskDialog td = new TaskDialog("WS Change Test — Stopped");
            td.MainContent =
                $"Captured {_docChangedCount} DocumentChanged event(s).\n\n" +
                "Log file:\n" + _logPath;
            td.Show();
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            _docChangedCount++;
            int added    = e.GetAddedElementIds().Count;
            int modified = e.GetModifiedElementIds().Count;
            int deleted  = e.GetDeletedElementIds().Count;
            string syncTag = _syncInProgress ? " [DURING-SYNC]" : "";
            AppendLog($"{Ts()}  DocumentChanged{syncTag}  +{added} ~{modified} -{deleted}\r\n");
        }

        private static void OnSynchronizing(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            _syncInProgress = true;
            AppendLog($"{Ts()}  DocumentSynchronizingWithCentral\r\n");
        }

        private static void OnSynchronized(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            _syncInProgress = false;
            AppendLog($"{Ts()}  DocumentSynchronizedWithCentral\r\n");
        }

        private static void OnReloadedLatest(object sender, DocumentReloadedLatestEventArgs e)
        {
            AppendLog($"{Ts()}  DocumentReloadedLatest\r\n");
        }

        private static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

        private static void AppendLog(string line)
        {
            try { File.AppendAllText(_logPath, line); }
            catch { }
        }
    }
}
