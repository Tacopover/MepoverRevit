using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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
    /// The orchestrator does not modify elements or open user-visible transactions.
    /// EnqueueRegenerateTask opens a short regeneration transaction (Revit-internal bookkeeping only).
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
        private bool _needsRegenerate = false;

        // Phase 4 — worksharing guard: true while a SWC is in progress.
        // All access on Revit main thread — no volatile needed.
        private bool _syncInProgress = false;
        private DateTime _syncStartedUtc = DateTime.MinValue;

        // ------------------------------------------------------------------ background writer (producer-consumer)
        private BlockingCollection<ElementGeometryDto> _dtoQueue;
        private Thread _writerThread;
        private CancellationTokenSource _writerCts;
        private int _backgroundWrittenCount; // written only by background thread via Interlocked

        // If OnSynchronizedWithCentral never fires (failed/cancelled SWC), auto-clear after this many seconds.
        private const int SyncTimeoutSeconds = 120;

        // Save cadence (seconds between periodic disk saves)
        private const int SaveIntervalSeconds = 5;

        // Time budget per idle tick for element batching (milliseconds)
        private const int BatchBudgetMs = 80;

        // Phase 4B — how often to poll the central file for non-plugin user changes
        private const int StalenessCheckIntervalSeconds = 60;

        // Quiet period: skip processing for this many seconds after a DocumentChanged event.
        private const double QuietPeriodSeconds = 2.0;

        public ExportState State => _state;

        /// <summary>
        /// True when the button should be enabled — session exists, initial export finished,
        /// and the orchestrator is in a state where the Store is valid.
        /// </summary>
        public bool IsReadyForViewExport =>
            _session?.InitialExportComplete == true
            && _session?.Store != null
            && (_state == ExportState.Running || _state == ExportState.Paused);

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
                IfcVersion = _viewModel.SelectedIfcVersion,
                PsetMappings = PsetMappingParser.Parse(_viewModel.PsetMappingFilePath)
            };

            // Phase 4B: resolve central file path at session start (main thread, before Idling).
            TryResolveCentralFilePath();

            // Enqueue the initialisation task — all actual work starts in OnIdling
            // so Revit API access is guaranteed to run on the main thread.
            EnqueueInitTask();

            _state = ExportState.Running;
            StartBackgroundWriter();
            _uiApp.Idling += OnIdling;
            SubscribeDocumentChanged();
            SubscribeWorksharingEvents();
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
                _syncInProgress = false;
                _tasks.Clear();
                _uiApp.Idling -= OnIdling;
                UnsubscribeDocumentChanged();
                UnsubscribeWorksharingEvents();
                StopBackgroundWriter(waitForCompletion: true);
                _session?.Dispose();
                _session = null;
                _viewModel.OnOrchestratorStateChanged();
            }
        }

        /// <summary>
        /// Synchronously updates the live session IFC with the elements visible in <paramref name="viewId"/>.
        /// For each visible element: removes the existing IFC entity (if any) then re-exports with
        /// fresh geometry. Saves the IFC file atomically after all elements are processed.
        /// Must be called on the Revit main thread (inside an ExternalEvent handler).
        /// </summary>
        /// <returns>A status string suitable for display in the ViewModel's StatusText.</returns>
        public string ExportActiveView(ElementId viewId, Document doc)
        {
            if (_syncInProgress)
                return "Export skipped — sync in progress. Try again after sync completes.";

            if (!IsReadyForViewExport)
                return "Error: Initial export not yet complete. Wait until monitoring mode starts.";

            var elements = new FilteredElementCollector(doc, viewId)
                .WhereElementIsNotElementType()
                .ToElements();

            int updated = 0;
            int added = 0;

            foreach (Element elem in elements)
            {
                try
                {
                    if (_session.ElementIdIndex.TryGetValue(elem.Id, out string uniqueId))
                    {
                        // Element already in IFC — remove old entity and maps, then re-export.
                        if (_session.ExportStateMap.TryGetValue(uniqueId, out string ifcGuid))
                        {
                            IfcElementWriter.RemoveElement(ifcGuid, _session);
                            _session.ExportStateMap.Remove(uniqueId);
                        }
                        _session.ElementIdIndex.Remove(elem.Id);
                        // Remove from dedup set so the element can be re-queued by DocumentChanged later.
                        _session.PendingModifiedIds.Remove(elem.Id);

                        string guid = IfcElementWriter.WriteElement(elem, _session);
                        if (guid != null) updated++;
                    }
                    else
                    {
                        // Element not yet in IFC — add it.
                        string guid = IfcElementWriter.WriteElement(elem, _session);
                        if (guid != null) added++;
                    }
                }
                catch
                {
                    // Skip bad elements — never abort the whole batch.
                }
            }

            // Atomic save: write to temp, then swap.
            string safeName = MakeSafeFileName(System.IO.Path.GetFileNameWithoutExtension(doc.Title ?? "export"));
            string target = System.IO.Path.Combine(_session.DestinationFolder, safeName + ".ifc");
            string temp = System.IO.Path.Combine(_session.DestinationFolder, safeName + "_partial.ifc");

            try
            {
                _session.Store.SaveAs(temp, Xbim.IO.StorageType.Ifc);

                if (System.IO.File.Exists(target))
                    System.IO.File.Delete(target);
                System.IO.File.Move(temp, target);

                _session.LastSaveUtc = DateTime.UtcNow;

                if (_session.CentralFilePath != null)
                {
                    try
                    {
                        _session.CentralFileTimestampAtLastSave =
                            System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (System.IO.File.Exists(temp))
                    System.IO.File.Delete(temp);
                return "Error saving IFC: " + ex.Message;
            }

            return string.Format(
                "View export complete — {0} updated, {1} added. Saved: {2}",
                updated, added, target);
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
                return; // Paused — let Revit throttle naturally

            // Auto-clear sync guard if no OnSynchronizedWithCentral arrived within SyncTimeoutSeconds.
            // Handles the case where SWC is cancelled or fails with no completion event.
            if (_syncInProgress &&
                _syncStartedUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _syncStartedUtc).TotalSeconds > SyncTimeoutSeconds)
            {
                _syncInProgress = false;
                if (_session != null)
                {
                    _session.LastDocumentChangedUtc = DateTime.UtcNow;
                    _viewModel.StatusText = "Sync timed out — resuming export…";
                }
            }

            if (_syncInProgress)
                return;

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

            // Transition handling when all tasks have drained.
            if (_tasks.Count == 0 && _state == ExportState.Running && _session != null)
            {
                bool initialDone = _session.TotalElements > 0
                                && _session.ElementQueue.Count == 0;

                if (initialDone && !_session.InitialExportComplete)
                {
                    // Initial full export finished — do a final save and enter monitoring mode.
                    _session.InitialExportComplete = true;
                    EnqueueSaveTask();
                    _tasks.Add(new IdleTask(_ =>
                    {
                        _viewModel.StatusText = "Monitoring for changes\u2026";
                        _viewModel.PendingChanges = 0;
                        return true;
                    }));
                }
                else if (_session.InitialExportComplete
                      && _session.PendingChangeQueue.Count > 0
                      && _session.RevitDocument != null)
                {
                    // Pending changes arrived while idle — regenerate geometry then drain.
                    EnqueueRegenerateTask(_session.RevitDocument);
                }
                else if (_session.InitialExportComplete
                      && _session.CentralFilePath != null
                      && _session.PendingChangeQueue.Count == 0
                      && (DateTime.UtcNow - _session.LastStalenessCheckUtc).TotalSeconds
                             >= StalenessCheckIntervalSeconds)
                {
                    // Phase 4B: periodic check for non-plugin user changes to the central model.
                    _session.LastStalenessCheckUtc = DateTime.UtcNow;
                    CheckCentralFileStaleness();
                }
            }
        }

        // ------------------------------------------------------------------ task factory helpers

        private void EnqueueInitTask()
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                Document doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null) return true; // no active document — skip gracefully

                if (string.IsNullOrEmpty(_session.OutputFilePath))
                {
                    string safeName = MakeSafeFileName(
                        System.IO.Path.GetFileNameWithoutExtension(doc.Title ?? "export"));
                    _session.OutputFilePath     = System.IO.Path.Combine(_session.DestinationFolder, safeName + ".ifc");
                    _session.OutputTempFilePath = System.IO.Path.Combine(_session.DestinationFolder, safeName + "_partial.ifc");
                }

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
                // Cache the document reference for the DocumentChanged handler and drain tasks.
                _session.RevitDocument = doc;

                // Collect only element types that can produce solid geometry.
                // Excludes Levels, Grids, Reference Planes, Rooms, Spaces, and other
                // non-geometry elements that would otherwise each waste an idle tick.
                // WriteElement still performs a geometry check as a safety net.
                var typeFilter = new ElementMulticlassFilter(new List<Type>
                {
                    typeof(Wall),
                    typeof(Floor),
                    typeof(Autodesk.Revit.DB.Architecture.Stairs),
                    typeof(Ceiling),
                    typeof(RoofBase),
                    typeof(FamilyInstance),  // MEP equipment, fittings, fixtures, doors, windows
                    typeof(MEPCurve),        // Duct, Pipe, CableTray, Conduit
                    typeof(DirectShape),
                    typeof(Part),
                    typeof(HostedSweep),
                });

                var collector = new FilteredElementCollector(doc)
                    .WherePasses(typeFilter)
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

        private void EnqueueRegenerateTask(Document doc)
        {
            _tasks.Add(new IdleTask(
                callback: uiApp =>
                {
                    _needsRegenerate = false;
                    using (var tx = new Transaction(doc, "Regenerate"))
                    {
                        tx.Start();
                        doc.Regenerate();
                        tx.Commit();
                    }
                    EnqueueDrainTask(doc);
                    return true;
                },
                readyCheck: IsReadyToProcess
            ));
        }

        private void EnqueueDrainTask(Document doc)
        {
            _tasks.Add(new IdleTask(
                callback: uiApp =>
                {
                    if (_session == null) return true;

                    bool moreWork;

                    // Pending changes: only process after initial export is complete.
                    // During initial export the background thread is the sole writer to the Xbim store.
                    if (_session.InitialExportComplete && _session.PendingChangeQueue.Count > 0)
                    {
                        PendingChange change = _session.PendingChangeQueue.Dequeue();
                        ProcessPendingChange(change, doc);
                        _viewModel.PendingChanges = _session.PendingChangeQueue.Count;
                        moreWork = (_session.PendingChangeQueue.Count > 0);
                    }
                    else if (_session.ElementQueue.Count > 0 && !_session.InitialExportComplete)
                    {
                        // Extract a batch of DTOs within the time budget and hand them to the background writer.
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (_session.ElementQueue.Count > 0 && sw.ElapsedMilliseconds < BatchBudgetMs)
                        {
                            ElementId id   = _session.ElementQueue.Peek();
                            Element   elem = doc.GetElement(id);
                            if (elem != null)
                            {
                                var dto = IfcElementWriter.ExtractDto(elem, _session.PsetMappings);
                                if (dto != null && _dtoQueue != null && !_dtoQueue.TryAdd(dto, 0))
                                    break; // DTO queue full — come back next tick
                            }
                            _session.ElementQueue.Dequeue();
                        }

                        if (_session.ElementQueue.Count == 0 && _dtoQueue != null && !_dtoQueue.IsAddingCompleted)
                            _dtoQueue.CompleteAdding(); // signal background thread: no more DTOs

                        // Keep drain alive: more elements to extract OR waiting for background thread to finish.
                        moreWork = (_session.ElementQueue.Count > 0 || !_session.InitialExportComplete);
                    }
                    else if (!_session.InitialExportComplete)
                    {
                        // All elements extracted; background thread still writing.
                        moreWork = true;
                    }
                    else
                    {
                        return true; // Nothing to do.
                    }

                    // Periodic disk save — only after initial export is complete (background thread owns saves during initial export).
                    if (_session.InitialExportComplete
                        && (DateTime.UtcNow - _session.LastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
                    {
                        EnqueueSaveTask();
                        _session.LastSaveUtc = DateTime.UtcNow;
                    }

                    if (moreWork)
                    {
                        if (_session.InitialExportComplete && _needsRegenerate && _session.PendingChangeQueue.Count > 0)
                            EnqueueRegenerateTask(doc);
                        else
                            EnqueueDrainTask(doc);
                    }

                    return true; // This drain-task instance is complete.
                },
                readyCheck: IsReadyToProcess
            ));
        }

        private void EnqueueSaveTask()
        {
            _tasks.Add(new IdleTask(uiApp =>
            {
                SaveIfcInternal();
                return true;
            }));
        }

        private void StartBackgroundWriter()
        {
            _writerCts              = new CancellationTokenSource();
            _dtoQueue               = new BlockingCollection<ElementGeometryDto>(boundedCapacity: 500);
            _backgroundWrittenCount = 0;

            _writerThread = new Thread(() => BackgroundWriterLoop(_writerCts.Token))
            {
                IsBackground = true,
                Name         = "IFC-Background-Writer"
            };
            _writerThread.Start();
        }

        private void StopBackgroundWriter(bool waitForCompletion)
        {
            if (_dtoQueue != null && !_dtoQueue.IsAddingCompleted)
                _dtoQueue.CompleteAdding();

            _writerCts?.Cancel();

            if (waitForCompletion)
                _writerThread?.Join(TimeSpan.FromSeconds(30));

            _dtoQueue?.Dispose();
            _dtoQueue     = null;
            _writerThread = null;
            _writerCts?.Dispose();
            _writerCts    = null;
        }

        private void SaveIfcInternal()
        {
            if (_session?.Store == null) return;
            string target = _session.OutputFilePath;
            string temp   = _session.OutputTempFilePath;
            if (string.IsNullOrEmpty(target)) return;

            try
            {
                _session.Store.SaveAs(temp, Xbim.IO.StorageType.Ifc);
                if (System.IO.File.Exists(target))
                    System.IO.File.Delete(target);
                System.IO.File.Move(temp, target);
                _session.LastSaveUtc = DateTime.UtcNow;

                if (_session.CentralFilePath != null)
                {
                    try
                    {
                        _session.CentralFileTimestampAtLastSave =
                            System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() => _viewModel.StatusText = "Save failed: " + ex.Message));
                if (System.IO.File.Exists(temp))
                    try { System.IO.File.Delete(temp); } catch { }
            }
        }

        private void BackgroundWriterLoop(CancellationToken ct)
        {
            var lastSave = DateTime.UtcNow;
            try
            {
                foreach (ElementGeometryDto dto in _dtoQueue.GetConsumingEnumerable(ct))
                {
                    string guid = IfcElementWriter.WriteElementFromDto(dto, _session);
                    if (guid != null)
                    {
                        int count = Interlocked.Increment(ref _backgroundWrittenCount);
                        int total = _session.TotalElements;
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            (Action)(() =>
                            {
                                _viewModel.ProgressValue = count;
                                _viewModel.StatusText    = string.Format("Writing {0} of {1} elements…", count, total);
                            }));
                    }

                    if ((DateTime.UtcNow - lastSave).TotalSeconds >= SaveIntervalSeconds)
                    {
                        SaveIfcInternal();
                        lastSave = DateTime.UtcNow; // update regardless of save success — prevents retry storm
                    }
                }
            }
            catch (OperationCanceledException) { /* expected on Cancel/Pause */ }

            if (!ct.IsCancellationRequested)
            {
                // Queue fully drained — initial export complete.
                SaveIfcInternal();
                int finalCount = _backgroundWrittenCount; // capture before dispatch

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() =>
                    {
                        _session.ExportedElements      = finalCount;
                        _session.InitialExportComplete = true;
                        _viewModel.ProgressValue = _session.TotalElements;
                        _viewModel.StatusText    = "Export complete — monitoring for changes.";
                        _viewModel.OnOrchestratorStateChanged();

                        // Kick off a drain task if document changes accumulated during initial export.
                        if (_session?.PendingChangeQueue.Count > 0 && _session.RevitDocument != null)
                            EnqueueDrainTask(_session.RevitDocument);
                    }));
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ------------------------------------------------------------------ Phase 4B — staleness detection

        /// <summary>
        /// Resolves the central model's filesystem path and stores it in the session.
        /// Called once from <see cref="Start"/> on the Revit main thread.
        /// Leaves <see cref="IfcExportSession.CentralFilePath"/> null when the document is
        /// not workshared, is cloud-hosted, or when the path is not accessible.
        /// </summary>
        private void TryResolveCentralFilePath()
        {
            if (_session == null) return;
            Document doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null || !doc.IsWorkshared) return;

            try
            {
                string centralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    doc.GetWorksharingCentralModelPath());
                if (System.IO.File.Exists(centralPath))
                    _session.CentralFilePath = centralPath;
            }
            catch { }
        }

        /// <summary>
        /// Compares the central Revit file's current last-write timestamp against the baseline
        /// recorded at the last IFC save (or SWC). If the file is newer, another user (without
        /// the plugin active) has modified and synced to central — trigger a full re-export.
        /// </summary>
        private void CheckCentralFileStaleness()
        {
            if (_session?.CentralFilePath == null) return;

            try
            {
                DateTime currentTs = System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);

                if (_session.CentralFileTimestampAtLastSave == DateTime.MinValue)
                {
                    // First check — just baseline without triggering re-export.
                    _session.CentralFileTimestampAtLastSave = currentTs;
                    return;
                }

                if (currentTs > _session.CentralFileTimestampAtLastSave)
                    TriggerFullReExport();
            }
            catch
            {
                // Central file temporarily inaccessible (network hiccup, etc.) — skip this cycle.
            }
        }

        /// <summary>
        /// Resets all export state and re-queues a full collection + export cycle.
        /// Called when the central model has been updated by a user without the plugin.
        ///
        /// False-negative window: changes made to the central model while the re-export is running
        /// (before the first IFC save completes) are not detected until the next staleness poll
        /// after that save. This is an accepted trade-off for the MVP.
        /// </summary>
        private void TriggerFullReExport()
        {
            if (_session?.RevitDocument == null) return;

            _viewModel.StatusText = "Central model updated by another user — re-exporting all elements…";
            _viewModel.ProgressValue = 0;
            _viewModel.ProgressMax = 1;
            _viewModel.PendingChanges = 0;

            // Critical: clear the stale-geometry flag before resetting tasks.
            // If _needsRegenerate is true when the new drain task runs, EnqueueRegenerateTask
            // fires before Initialize() has created a new Store → drain writes to null Store.
            _needsRegenerate = false;

            // Stop the background writer before resetting — it holds live references to the store.
            StopBackgroundWriter(waitForCompletion: true);

            _session.ResetForFullReExport();

            // Immediately baseline the central file timestamp so the 60-second poll window
            // after this trigger can still catch a second remote commit during the re-export.
            if (_session.CentralFilePath != null)
            {
                try
                {
                    _session.CentralFileTimestampAtLastSave =
                        System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);
                }
                catch { }
            }

            _tasks.Clear();
            EnqueueInitTask();
            StartBackgroundWriter();
        }

        // ------------------------------------------------------------------ Phase 3 — change tracking

        private void SubscribeDocumentChanged()
        {
            _uiApp.Application.DocumentChanged += OnDocumentChanged;
        }

        private void UnsubscribeDocumentChanged()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
        }

        // ------------------------------------------------------------------ Phase 4 — worksharing events

        private void SubscribeWorksharingEvents()
        {
            _uiApp.Application.DocumentSynchronizingWithCentral += OnSynchronizingWithCentral;
            _uiApp.Application.DocumentSynchronizedWithCentral += OnSynchronizedWithCentral;
            _uiApp.Application.DocumentReloadedLatest += OnDocumentReloadedLatest;
        }

        private void UnsubscribeWorksharingEvents()
        {
            _uiApp.Application.DocumentSynchronizingWithCentral -= OnSynchronizingWithCentral;
            _uiApp.Application.DocumentSynchronizedWithCentral -= OnSynchronizedWithCentral;
            _uiApp.Application.DocumentReloadedLatest -= OnDocumentReloadedLatest;
        }

        private void OnSynchronizingWithCentral(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            // If we already know the export document, ignore syncs from other open documents.
            // When RevitDocument is null (collect task hasn't run yet), accept conservatively.
            if (_session?.RevitDocument != null &&
                e.Document?.Title != _session.RevitDocument.Title)
                return;

            _syncInProgress = true;
            _syncStartedUtc = DateTime.UtcNow;
            if (_session != null)
                _viewModel.StatusText = "Syncing with central — export paused…";
        }

        private void OnSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            if (_session?.RevitDocument != null &&
                e.Document?.Title != _session.RevitDocument.Title)
                return;

            _syncInProgress = false;
            // _session null-check is load-bearing: Cancel() disposes the session and the event
            // can fire one tick later. Do not remove this guard.
            if (_session != null)
            {
                _session.LastDocumentChangedUtc = DateTime.UtcNow;
                _viewModel.StatusText = "Sync complete — resuming export…";

                // Phase 4B: re-baseline central file timestamp after our own SWC so the
                // staleness check does not fire for our own changes.
                if (_session.CentralFilePath != null)
                {
                    try
                    {
                        _session.CentralFileTimestampAtLastSave =
                            System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);
                    }
                    catch { }
                }
            }
        }

        private void OnDocumentReloadedLatest(object sender, DocumentReloadedLatestEventArgs e)
        {
            if (_session?.RevitDocument != null &&
                e.Document?.Title != _session.RevitDocument.Title)
                return;

            // _session null-check is load-bearing — see OnSynchronizedWithCentral.
            if (_session != null)
                _session.LastDocumentChangedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Fires on the Revit main thread whenever the active document changes.
        /// Queues <see cref="PendingChange"/> entries for processing in subsequent idle ticks.
        /// Deleted entries resolve their UniqueId here from <see cref="IfcExportSession.ElementIdIndex"/>
        /// before the element is gone from the document.
        /// </summary>
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (_session == null || _state != ExportState.Running) return;

            // Always reset the quiet-period clock and flag geometry as stale.
            _session.LastDocumentChangedUtc = DateTime.UtcNow;
            _needsRegenerate = true;

            // During SWC, Revit fires DocumentChanged for workset/ownership operations that are not
            // meaningful geometry edits. Skip enqueuing — OnSynchronizedWithCentral handles the
            // quiet-period reset and any real changes will arrive via the subsequent reconciliation sweep.
            if (_syncInProgress) return;

            // Wait until the collect task has run and stored the document reference.
            if (_session.RevitDocument == null) return;

            // Ignore events for other documents (e.g. linked files).
            Document changedDoc = e.GetDocument();
            if (changedDoc.Title != _session.RevitDocument.Title) return;

            // Deleted — resolve UniqueId now while the index still holds the entry.
            foreach (ElementId id in e.GetDeletedElementIds())
            {
                if (_session.ElementIdIndex.TryGetValue(id, out string uniqueId))
                {
                    _session.PendingChangeQueue.Enqueue(
                        new PendingChange(ChangeType.Deleted, id, uniqueId));
                    _session.ElementIdIndex.Remove(id);
                }
            }

            // Modified — expand directly-reported elements to include connected/joined neighbours,
            // then deduplicate so the same element is never queued twice.
            var directlyModified = new HashSet<ElementId>(e.GetModifiedElementIds());
            var allModified = new HashSet<ElementId>(directlyModified);
            foreach (ElementId id in directlyModified)
            {
                Element elem = changedDoc.GetElement(id);
                if (elem == null) continue;
                foreach (ElementId related in GetRelatedElementIds(changedDoc, elem))
                    allModified.Add(related);
            }

            foreach (ElementId id in allModified)
            {
                if (!_session.ElementIdIndex.ContainsKey(id)) continue;
                if (_session.PendingModifiedIds.Contains(id)) continue;
                _session.PendingChangeQueue.Enqueue(new PendingChange(ChangeType.Modified, id));
                _session.PendingModifiedIds.Add(id);
            }

            // Added — only elements not already in the session.
            foreach (ElementId id in e.GetAddedElementIds())
            {
                if (!_session.ElementIdIndex.ContainsKey(id))
                    _session.PendingChangeQueue.Enqueue(
                        new PendingChange(ChangeType.Added, id));
            }

            _viewModel.PendingChanges = _session.PendingChangeQueue.Count;
        }

        /// <summary>
        /// Returns ElementIds of elements structurally or network-connected to <paramref name="elem"/>
        /// that may have moved as a side-effect of modifying <paramref name="elem"/>.
        /// Covers: geometry-joined elements (walls etc.) and MEP connector networks
        /// (pipes, ducts, cable trays, conduits, fittings, equipment).
        /// </summary>
        private static IEnumerable<ElementId> GetRelatedElementIds(Document doc, Element elem)
        {
            var result = new List<ElementId>();

            // Geometry-joined elements (walls joined to walls, beams, slabs, etc.)
            try
            {
                foreach (ElementId id in JoinGeometryUtils.GetJoinedElements(doc, elem))
                    result.Add(id);
            }
            catch { }

            // MEP connector network — pipes, ducts, cable trays, conduits
            ConnectorManager cm = null;
            if (elem is MEPCurve mepCurve)
                cm = mepCurve.ConnectorManager;
            else if (elem is FamilyInstance fi && fi.MEPModel != null)
                cm = fi.MEPModel.ConnectorManager;

            if (cm != null)
            {
                foreach (Connector connector in cm.Connectors)
                {
                    try
                    {
                        foreach (Connector reference in connector.AllRefs)
                        {
                            Element owner = reference.Owner;
                            if (owner != null && owner.Id != elem.Id)
                                result.Add(owner.Id);
                        }
                    }
                    catch { }
                }
            }

            // Hosted/dependent elements: doors in walls, windows, fixtures in ceilings,
            // pipe accessories, etc. Safety net for side-effect moves Revit may not
            // explicitly report in GetModifiedElementIds.
            try
            {
                foreach (ElementId depId in elem.GetDependentElements(null))
                {
                    Element dep = doc.GetElement(depId);
                    if (dep == null) continue;
                    if (dep.ViewSpecific) continue;   // skip tags, annotations, dimensions
                    if (dep is ElementType) continue; // skip element type catalog entries
                    result.Add(depId);
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Returns true when it is safe to process elements — i.e., no <c>DocumentChanged</c>
        /// event has fired within the last <see cref="QuietPeriodSeconds"/> seconds.
        /// </summary>
        private bool IsReadyToProcess()
        {
            if (_session == null) return true;
            if (_session.LastDocumentChangedUtc == DateTime.MinValue) return true;
            return (DateTime.UtcNow - _session.LastDocumentChangedUtc).TotalSeconds >= QuietPeriodSeconds;
        }

        private void ProcessPendingChange(PendingChange change, Document doc)
        {
            try
            {
                switch (change.Type)
                {
                    case ChangeType.Added: ProcessAdded(change.ElementId, doc); break;
                    case ChangeType.Modified: ProcessModified(change.ElementId, doc); break;
                    case ChangeType.Deleted: ProcessDeleted(change.UniqueId); break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("ProcessPendingChange failed ({0} {1}): {2}",
                        change.Type, change.ElementId, ex.Message));
            }
        }

        private void ProcessAdded(ElementId id, Document doc)
        {
            // Skip if the element was somehow already exported.
            if (_session.ElementIdIndex.ContainsKey(id)) return;

            Element elem = doc.GetElement(id);
            if (elem == null) return;

            IfcElementWriter.WriteElement(elem, _session);
            _viewModel.StatusText = string.Format("Added: {0}", elem.Name ?? id.ToString());
        }

        private void ProcessModified(ElementId id, Document doc)
        {
            // Release the dedup slot so this element can be re-queued if modified again later.
            _session.PendingModifiedIds.Remove(id);

            if (!_session.ElementIdIndex.TryGetValue(id, out string uniqueId)) return;
            if (!_session.ExportStateMap.TryGetValue(uniqueId, out string ifcGuid)) return;

            // Remove old IFC entity and clean up maps.
            IfcElementWriter.RemoveElement(ifcGuid, _session);
            _session.ExportStateMap.Remove(uniqueId);
            _session.ElementIdIndex.Remove(id);

            // Re-export with the current geometry.
            Element elem = doc.GetElement(id);
            if (elem == null) return;

            IfcElementWriter.WriteElement(elem, _session);
            _viewModel.StatusText = string.Format("Updated: {0}", elem.Name ?? id.ToString());
        }

        private void ProcessDeleted(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId)) return;
            if (!_session.ExportStateMap.TryGetValue(uniqueId, out string ifcGuid)) return;

            IfcElementWriter.RemoveElement(ifcGuid, _session);
            _session.ExportStateMap.Remove(uniqueId);
            // ElementIdIndex entry was already removed in OnDocumentChanged.

            _viewModel.StatusText = string.Format("Deleted element (uid: {0})", uniqueId);
        }
    }
}
