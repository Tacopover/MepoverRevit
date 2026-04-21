using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;

namespace IfcExport
{
    public enum ExportState
    {
        Idle,
        Running,
        Paused,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Manages the export session lifecycle, owns the Idling event subscription,
    /// and drives the deferred task pump.
    ///
    /// All Revit API access happens inside <see cref="OnIdling"/> (main thread).
    /// The orchestrator is strictly read-only with respect to the Revit document.
    ///
    /// Task-pump pattern (per plan §Deferred Task Pump):
    ///   - The task list is iterated with a plain for-loop so callbacks may enqueue follow-ups.
    ///   - Each task sets Completed = true on all terminal paths.
    ///   - Completed tasks are removed each tick.
    /// </summary>
    public class IdleExportOrchestrator
    {
        private readonly UIApplication _uiApp;
        private readonly IfcExportViewModel _viewModel;

        private ExportState _state = ExportState.Idle;
        private IfcExportSession _session;

        // Task pump
        private readonly List<IdleTask> _tasks = new List<IdleTask>();

        // Save cadence (seconds between periodic disk saves)
        private const int SaveIntervalSeconds = 5;

        public ExportState State => _state;

        /// <summary>Surfaces an error message in the ViewModel's status text.</summary>
        public void ReportError(string message)
        {
            _viewModel.StatusText = message;
        }

        public IdleExportOrchestrator(UIApplication uiApp, IfcExportViewModel viewModel)
        {
            _uiApp = uiApp;
            _viewModel = viewModel;
        }

        // ------------------------------------------------------------------ public API

        public void Start()
        {
            if (_state != ExportState.Idle)
                return;

            _session = new IfcExportSession
            {
                DestinationFolder = _viewModel.DestinationFolder,
                IfcVersion = _viewModel.SelectedIfcVersion
            };

            // Enqueue the initialisation task — all actual work starts in OnIdling
            // so Revit API access is guaranteed to run on the main thread.
            EnqueueInitTask();

            _state = ExportState.Running;
            _uiApp.Idling += OnIdling;
            _viewModel.OnOrchestratorStateChanged();
        }

        public void Pause()
        {
            if (_state != ExportState.Running)
                return;
            _state = ExportState.Paused;
            _viewModel.OnOrchestratorStateChanged();
        }

        public void Resume()
        {
            if (_state != ExportState.Paused)
                return;
            _state = ExportState.Running;
            _viewModel.OnOrchestratorStateChanged();
        }

        public void Cancel()
        {
            if (_state == ExportState.Running || _state == ExportState.Paused)
            {
                _state = ExportState.Cancelled;
                _tasks.Clear();
                _uiApp.Idling -= OnIdling;
                _session?.Dispose();
                _session = null;
                _viewModel.OnOrchestratorStateChanged();
            }
        }

        // ------------------------------------------------------------------ Idling handler

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_state == ExportState.Cancelled || _state == ExportState.Completed)
            {
                _uiApp.Idling -= OnIdling;
                return;
            }

            if (_state != ExportState.Running)
                return; // Paused — Revit will throttle the event naturally

            // Call with no args = "fire again immediately"; omit = Revit default cadence.
            if (_tasks.Count > 0)
                e.SetRaiseWithoutDelay();
            try
            {
                PumpTasks((UIApplication)sender);
            }
            catch (Exception ex)
            {
                // Graceful degradation: surface the error in UI, do not crash Revit.
                _viewModel.StatusText = "Error: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ task pump

        private void PumpTasks(UIApplication uiApp)
        {
            // Index-based loop so callbacks may enqueue follow-up tasks safely.
            for (int i = 0; i < _tasks.Count; i++)
            {
                IdleTask task = _tasks[i];
                if (!task.Completed && task.IsReady())
                {
                    task.Eval(uiApp);
                    break; // one task per tick
                }
            }

            // Remove completed tasks.
            _tasks.RemoveAll(t => t.Completed);

            // Transition to Completed when nothing remains.
            // Enqueue a final save followed by a completion task so the last elements
            // are guaranteed to reach disk before the session is disposed.
            if (_tasks.Count == 0 && _state == ExportState.Running
                && _session != null && _session.TotalElements > 0
                && _session.ElementQueue.Count == 0)
            {
                EnqueueSaveTask();
                _tasks.Add(new IdleTask(_ =>
                {
                    _state = ExportState.Completed;
                    _uiApp.Idling -= OnIdling;
                    _session.Dispose();
                    _session = null;
                    _viewModel.OnOrchestratorStateChanged();
                    return true;
                }));
            }
        }

        // ------------------------------------------------------------------ task factory helpers

        private void EnqueueInitTask()
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                Document doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null) return true; // no active document — skip gracefully

                bool done = IfcProjectInitializer.Initialize(_session, doc);
                if (done)
                    EnqueueCollectTask(doc);
                return done;
            }));
        }

        private void EnqueueCollectTask(Document doc)
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                // Collect all view-independent element instances (geometry filter applied
                // per element in IfcElementWriter to avoid category whitelist maintenance).
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent();

                foreach (Element elem in collector)
                    _session.ElementQueue.Enqueue(elem.Id);

                _session.TotalElements = _session.ElementQueue.Count;
                _viewModel.ProgressMax = _session.TotalElements > 0 ? _session.TotalElements : 1;
                _viewModel.StatusText = string.Format("Collected {0} elements — starting export", _session.TotalElements);

                // Seed the drain loop.
                EnqueueDrainTask(doc);
                return true;
            }));
        }

        private void EnqueueDrainTask(Document doc)
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                if (_session == null || _session.ElementQueue.Count == 0)
                    return true;

                ElementId id = _session.ElementQueue.Dequeue();
                Element elem = doc.GetElement(id);

                if (elem != null)
                {
                    // Phase 2: IfcElementWriter.WriteElement fills in the IFC geometry.
                    IfcElementWriter.WriteElement(elem, _session);
                    _session.ExportedElements++;

                    _viewModel.ProgressValue = _session.ExportedElements;
                    _viewModel.StatusText = string.Format(
                        "Exporting element {0} of {1} \u2014 {2}",
                        _session.ExportedElements,
                        _session.TotalElements,
                        elem.Name ?? string.Empty);
                }

                // Periodic disk save.
                if ((DateTime.UtcNow - _session.LastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
                {
                    EnqueueSaveTask();
                    _session.LastSaveUtc = DateTime.UtcNow;
                }

                // Re-enqueue self for the next element next tick.
                if (_session.ElementQueue.Count > 0)
                    EnqueueDrainTask(doc);

                return true; // this drain-task instance is complete
            }));
        }

        private void EnqueueSaveTask()
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                if (_session?.Store == null)
                    return true;

                string folder   = _session.DestinationFolder;
                string docTitle = uiApp.ActiveUIDocument?.Document?.Title ?? "export";
                // Strip any existing extension from the title (e.g. "project.ifc" → "project")
                // to prevent double extensions like "project.ifc.ifc" in the output filename.
                string safeName = MakeSafeFileName(System.IO.Path.GetFileNameWithoutExtension(docTitle));
                string target   = System.IO.Path.Combine(folder, safeName + ".ifc");
                // Temp path must end in .ifc: Xbim.SaveAs appends ".ifc" when the path
                // does not already have that extension (confirmed by IfcQuickExporter).
                string temp     = System.IO.Path.Combine(folder, safeName + "_partial.ifc");

                try
                {
                    _session.Store.SaveAs(temp, Xbim.IO.StorageType.Ifc);

                    // Atomic swap: only replace the target after a successful write.
                    if (System.IO.File.Exists(target))
                        System.IO.File.Delete(target);
                    System.IO.File.Move(temp, target);

                    _session.LastSaveUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _viewModel.StatusText = "Save failed: " + ex.Message;
                    if (System.IO.File.Exists(temp))
                        System.IO.File.Delete(temp);
                }

                return true;
            }));
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
