using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
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
        private bool _needsRegenerate = false;

        // Save cadence (seconds between periodic disk saves)
        private const int SaveIntervalSeconds = 5;

        // Quiet period: skip processing for this many seconds after a DocumentChanged event.
        private const double QuietPeriodSeconds = 2.0;

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
                IfcVersion        = _viewModel.SelectedIfcVersion,
                PsetMappings      = PsetMappingParser.Parse(_viewModel.PsetMappingFilePath)
            };

            // Enqueue the initialisation task — all actual work starts in OnIdling
            // so Revit API access is guaranteed to run on the main thread.
            EnqueueInitTask();

            _state = ExportState.Running;
            _uiApp.Idling += OnIdling;
            SubscribeDocumentChanged();
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
                UnsubscribeDocumentChanged();
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
                        _viewModel.StatusText      = "Monitoring for changes\u2026";
                        _viewModel.PendingChanges  = 0;
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
                // Cache the document reference for the DocumentChanged handler and drain tasks.
                _session.RevitDocument = doc;

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

                    if (_session.PendingChangeQueue.Count > 0)
                    {
                        // Priority: process one pending change before the initial export queue.
                        PendingChange change = _session.PendingChangeQueue.Dequeue();
                        ProcessPendingChange(change, doc);
                        _viewModel.PendingChanges = _session.PendingChangeQueue.Count;
                        moreWork = (_session.PendingChangeQueue.Count > 0
                                 || _session.ElementQueue.Count > 0);
                    }
                    else if (_session.ElementQueue.Count > 0)
                    {
                        ElementId id = _session.ElementQueue.Dequeue();
                        Element elem = doc.GetElement(id);
                        if (elem != null)
                        {
                            IfcElementWriter.WriteElement(elem, _session);
                            _session.ExportedElements++;
                            _viewModel.ProgressValue = _session.ExportedElements;
                            _viewModel.StatusText = string.Format(
                                "Exporting element {0} of {1} \u2014 {2}",
                                _session.ExportedElements,
                                _session.TotalElements,
                                elem.Name ?? string.Empty);
                        }
                        moreWork = (_session.ElementQueue.Count > 0
                                 || _session.PendingChangeQueue.Count > 0);
                    }
                    else
                    {
                        return true; // Nothing to do — task complete.
                    }

                    // Periodic disk save.
                    if ((DateTime.UtcNow - _session.LastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
                    {
                        EnqueueSaveTask();
                        _session.LastSaveUtc = DateTime.UtcNow;
                    }

                    if (moreWork)
                    {
                        if (_needsRegenerate && _session.PendingChangeQueue.Count > 0)
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

        // ------------------------------------------------------------------ Phase 3 — change tracking

        private void SubscribeDocumentChanged()
        {
            _uiApp.Application.DocumentChanged += OnDocumentChanged;
        }

        private void UnsubscribeDocumentChanged()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
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
                    case ChangeType.Added:    ProcessAdded(change.ElementId, doc);  break;
                    case ChangeType.Modified: ProcessModified(change.ElementId, doc); break;
                    case ChangeType.Deleted:  ProcessDeleted(change.UniqueId);        break;
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
