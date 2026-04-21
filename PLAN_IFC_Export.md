# Master Plan: Incremental IFC Export During Idle Time

## Feature Summary

A background IFC exporter that activates via a ribbon button, runs progressively during Revit idle time, exports the full 3D geometry and minimal properties of all geometry-bearing elements in the active host document, and keeps the output file synchronized with model changes. In workshared models, remote-user updates are handled using cooperative client updates plus periodic reconciliation (best-effort, then verify). The user controls the export via a dedicated dialog with real-time progress feedback.

---

## Development Guard Rails

These principles govern **all** implementation work on this feature. When in doubt, refer back to this section.

### Architectural Constraints

1. **All Revit API access happens on the main thread.** The `Idling` event handler runs on Revit's main thread — use it for all element reads and geometry extraction. Never spawn background threads that call into the Revit API.
2. **The exporter is strictly read-only.** It must never open transactions, modify elements, or change the document in any way. If a Revit API call requires a transaction, it is the wrong API call for this feature.
3. **One element per idle tick.** Process exactly one element (or one pending change) per `Idling` callback. This keeps the UI responsive. If profiling later shows that more can be processed without lag, increase the batch size — but start with one.
4. **Graceful skip, never crash.** If geometry extraction fails for an element (null geometry, unsupported type, exception), log it, skip the element, and continue. The export must never crash Revit or leave a corrupted IFC file.
5. **The IFC file on disk must always be valid.** After each save, the `.ifc` file must be openable in an IFC viewer. If a save fails, the previous valid file must not be overwritten — write to a temp file and swap on success.
6. **Follow existing codebase patterns.** Use the SheetCopier modeless dialog pattern (`ExternalEvent` + `RequestHandler` + `WindowInteropHelper.Owner`). Inherit from `BaseViewModel`. Use `RelayCommand<T>`. Add new files to `MepoverSharedProject.projitems`.
7. **Single writer at a time for one IFC file.** Multiple users may target the same IFC path, but writes must be serialized via a lock/lease file. Concurrent writes to one IFC file are forbidden.
8. **Idling handler stays thin.** Use a deferred task queue (task pump) so `Idling` only evaluates readiness and executes short units of work.

### Scope Boundaries

9. **Host document only.** Do not attempt to export elements from linked Revit models, even if it seems straightforward.
10. **Minimal properties only.** Export only the "Revit Identity" property set (Name, Category, Family/Type, ElementId, UniqueId, Level). Do not add additional properties without an explicit plan update.
11. **Tessellation only.** Use `Face.Triangulate()` → `IfcFacetedBrep` / `IfcTriangulatedFaceSet`. Do not implement extrusion detection (Option B) or analytical geometry in the initial implementation.
12. **No UI beyond the specified dialog.** Do not add settings pages, option dialogs, category filters, or additional ribbon buttons. The dialog spec in this document is the complete UI.
13. **Initial baseline export is mandatory.** Incremental/cooperative updates are only allowed after a known-good full export has completed.

### Quality Gates

14. **Each implementation phase must be verified independently** before moving to the next. See the Phases section for specific "done when" criteria.
15. **IFC output must be validated in an IFC viewer** (e.g. Xbim Xplorer, BIMvision, or FZK Viewer) after each phase that changes geometry or structure output.
16. **Test with both workshared and non-workshared models** once worksharing support is implemented (Phase 4).
17. **Run a 2+ user contention test** (same central model, same IFC destination) to verify lock behavior and no file corruption.

### Anti-Patterns to Avoid

18. **Do not save to disk after every single element.** Save periodically — every N elements (start with 10) or every N seconds (start with 5). Saving is expensive; the IfcStore in memory is the source of truth between saves.
19. **Do not pre-filter elements by category list.** Use geometry presence as the sole filter. Maintaining a category whitelist/blacklist adds complexity and misses edge cases.
20. **Do not try to merge or diff IFC files.** The `IfcStore` in memory is the single mutable model. All changes go through it. The file on disk is just a periodic snapshot.
21. **Do not introduce multithreading or async/await for Revit API work.** The `Idling` handler is the only safe execution context.

---

## Decisions Log

Settled decisions that must not be revisited without updating this plan.

| #   | Question                  | Decision                                                                                                     |
| --- | ------------------------- | ------------------------------------------------------------------------------------------------------------ |
| 1   | Tessellation detail level | `ViewDetailLevel.Fine`, hardcoded. Can be made configurable in a future iteration.                           |
| 2   | Element scope             | All categories with geometry. No category filter UI.                                                         |
| 3   | Properties                | Minimal set only (see "Minimal Properties" section).                                                         |
| 4   | Linked models             | Host document only. Out of scope.                                                                            |
| 5   | Resume behaviour          | Identical mechanism for idle-pause resume and close/reopen resume — both load the JSON sidecar and continue. |
| 6   | Geometry approach         | Tessellation (Option A) only. Extrusion detection (Option B) deferred to a future iteration.                 |
| 7   | Save frequency            | Periodic (every N elements / N seconds), not per-element.                                                    |
| 8   | Shared IFC multi-user     | Supported in cooperative mode with strict single-writer lock/lease per target IFC file.                      |
| 9   | DocumentChanged semantics | Treat `DocumentChanged` as local-user only for this add-in; never use it as remote-user evidence.            |
| 10  | Remote change detection   | Detect remote-user changes via reconciliation sweeps, not via lifecycle event payload assumptions.           |

---

## Implementation Phases

### Phase 1 — Skeleton & Lifecycle

Set up the project structure, ribbon button, dialog, and orchestrator lifecycle without any actual export logic.

**Work:**

- Add `IfcExportCommand` (`IExternalCommand`) to shared project
- Register "IFC Export" `PushButton` in `RevitApplication.AddRibbonPanel()`
- Create `IfcExportDialog` (modeless WPF window) with settings fields, progress area, and Run/Pause/Cancel buttons — follow SheetCopier pattern for modeless hosting
- Create `IfcExportViewModel` inheriting `BaseViewModel` with `RelayCommand<T>` for Run/Pause/Cancel
- Create `IdleExportOrchestrator` shell with state machine (Idle → Running → Paused → Completed → Cancelled)
- Wire up orchestrator subscribe/unsubscribe on Run/Cancel
- Register new files in `MepoverSharedProject.projitems`

**Done when:** The ribbon button opens the dialog. Run/Pause/Cancel toggle the orchestrator state. The dialog can be closed and re-opened. No export happens yet.

---

### Phase 2 — Initial Full Export

Implement the core export pipeline: collect elements, extract geometry, write IFC.

**Work:**

- Add `Xbim.Essentials` NuGet package to all version projects
- Implement element collection: `FilteredElementCollector` → filter to instances with geometry
- Implement `IfcElementWriter.ExtractRevitGeometry()`: `get_Geometry(Options{DetailLevel=Fine})` → `Solid` → `Face.Triangulate()` → vertex/triangle lists
- Implement `IfcElementWriter.WriteIfcGeometry()`: create `IfcFacetedBrep` (IFC2x3) or `IfcTriangulatedFaceSet` (IFC4+) via Xbim
- Implement `IfcElementWriter.WriteIfcPlacement()`: Revit transform → `IfcLocalPlacement`, feet → metres (×0.3048)
- Implement `IfcElementWriter.WriteIfcProperties()`: write the minimal "Revit Identity" `IfcPropertySet`
- Create IFC project hierarchy on Run: `IfcProject` → `IfcSite` → `IfcBuilding` → `IfcBuildingStorey`
- Implement the `Idling` handler loop: dequeue element → write to IfcStore → update maps → periodic save
- Implement progress reporting back to the ViewModel

**Done when:** Clicking Run exports all geometry-bearing elements to an `.ifc` file. The file opens in an IFC viewer with correct geometry and the minimal property set. Progress bar reflects actual progress.

---

### Phase 3 — Change Tracking

Detect model changes and update the IFC file incrementally.

**Work:**

- Subscribe to `ControlledApplication.DocumentChanged`
- Implement the `PendingChanges` queue for **local-user edits only**: collect added/modified/deleted element IDs from each event
- Implement the dual map: `ExportStateMap` (`UniqueId → IfcGuid`) and `ElementIdIndex` (`ElementId → UniqueId`)
- Implement change processing in the `Idling` handler: pending changes take priority over the initial export queue
- Handle **Added**: export element, insert IFC entity, update both maps
- Handle **Modified**: look up via `ElementIdIndex` → remove old IFC entity → re-export → update maps
- Handle **Deleted**: look up via `ElementIdIndex` → remove IFC entity → remove from both maps
- Implement quiet period logic: skip processing if `DocumentChanged` fired within last 2 seconds
- Add lightweight reconciliation markers (e.g., `UniqueId -> lastExportFingerprint`) to detect stale IFC entities without relying on remote event payloads

**Done when:** Modifying an element in Revit updates its geometry in the IFC file. Adding new elements adds them. Deleting elements removes them. If an expected event is missed, reconciliation detects and repairs drift. Verified by re-opening the IFC in a viewer after each type of change.

---

### Phase 4 — Workshared Model Support

Handle changes arriving from other users in workshared models.

**Work:**

- Subscribe to `ControlledApplication.DocumentSynchronizingWithCentral` → set `_syncInProgress = true`
- Subscribe to `ControlledApplication.DocumentSynchronizedWithCentral` → clear flag, reset quiet period
- Subscribe to `ControlledApplication.DocumentReloadedLatest` → clear flag, reset quiet period
- Ensure `Idling` handler skips processing while `_syncInProgress` is true
- Keep using `DocumentChanged` for local edits only while workshared mode is active
- Add periodic reconciliation sweep after Reload Latest / SWC completion:
  - Scan a bounded subset first (recently touched/exported elements)
  - Escalate to chunked full-model reconciliation over idle ticks when confidence is low
  - Re-export elements whose fingerprints differ from sidecar state
- Implement cooperative multi-user mode for same IFC target:
  - Every client only queues local user edits immediately
  - Remote edits are eventually corrected by reconciliation
  - Acquire lock/lease file (e.g. `<file>.ifc.lock`) before save; if lock unavailable, defer save and retry on later idle ticks
  - Lock has timestamp + owner identity; stale lock recovery with timeout

**Done when:** In a workshared model, each user can run the exporter to the same IFC destination without corruption, writes are serialized by lock, and remote changes not captured directly by events are corrected by reconciliation. Export pauses during sync and resumes after. Tested with at least two users on the same central model.

---

### Phase 5 — Session Persistence & Resume

Allow export sessions to survive Revit restarts.

**Work:**

- Implement sidecar file serialisation: write `ExportStateMap` + metadata to `<filename>.ifc.json` alongside the IFC file
- Save sidecar on each periodic disk save and on session cancel/close
- On Run, check for existing sidecar + IFC file at the chosen destination
- If found: load `IfcStore` from existing IFC, load `ExportStateMap` from sidecar, rebuild `ElementIdIndex` from live document, resume from where left off
- If not found or mismatched: start fresh
- Handle edge case: sidecar exists but IFC file is missing or corrupted → start fresh
- Subscribe to `DocumentClosing` to auto-save sidecar before session ends

**Done when:** Closing Revit mid-export and re-opening the model allows resuming the export from where it left off. The sidecar correctly records and restores state. Corrupted/missing files are handled gracefully.

---

## UI / User Flow

### Ribbon Button

- A new **"IFC Export"** `PushButton` is added to the existing `MEPover` ribbon panel in `RevitApplication.cs` (alongside the existing SheetCopier button).
- Clicking it opens (or re-opens/focuses) the IFC Export dialog.
- The dialog can be opened at any point — before, during, or after an export session.

### IFC Export Dialog

| Section      | Controls                                                                                                                                                  |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Settings** | IFC version dropdown (IFC2x3 / IFC4 / IFC4.3), destination folder picker (text box + browse button)                                                       |
| **Progress** | Progress bar, status text (e.g. "Exporting element 450 of 3200 — Duct 412"), count of pending changes                                                     |
| **Controls** | **Run** button (starts the export session), **Pause** button (suspends idle processing), **Cancel** button (stops and optionally deletes the output file) |

> The export does **not** start until the user fills in the settings and clicks **Run**.  
> The dialog can be closed and re-opened at any time without interrupting an active export.  
> The **Pause** button toggles — pressing it again resumes.

---

## Technical Design

### Idle Time Detection

**Mechanism: Revit's `Idling` Event**

- `UIApplication.Idling` fires repeatedly when Revit is not processing user input or transactions.
- Subscribe on **Run** click; unsubscribe (or suppress) on **Cancel**.
- `IdlingEventArgs.SetRaiseWithoutDelay(true)` keeps the event firing continuously during active export. Set to `false` when paused.
- Keep the handler minimal: evaluate deferred tasks, run only ready work items, and remove completed items.

**Preventing Export During Active Model Edits**

- Subscribe to `ControlledApplication.DocumentChanged` to detect when the model is being modified.
- Track a **"last edit timestamp"**.
- In the `Idling` handler: only proceed if a quiet period (2 seconds) has passed since the last `DocumentChanged` event.

**Export Session Lifetime**

- Session state (Idle / Running / Paused / Completed / Cancelled) is held in the `IdleExportOrchestrator`.
- Created on **Run**, disposed on **Cancel** or `DocumentClosing`.
- On `DocumentClosing`: auto-save sidecar, cancel session gracefully.

---

### Incremental IFC File Creation (Xbim.Essentials)

**[Xbim.Essentials](https://github.com/xBimTeam/XbimEssentials)** — .NET Standard 2.0 library (CDDL licence) for creating, reading, and modifying IFC files. Supports IFC2x3, IFC4, and IFC4.3.

**Sequential export flow:**

1. On **Run**, create a new `IfcStore` (or open existing when resuming) and write `IfcProject` → `IfcSite` → `IfcBuilding` → `IfcBuildingStorey` once.
2. During each `Idling` tick, process one element from the queue.
3. Create the IFC entity (geometry + properties) and insert into `IfcStore`.
4. Periodically flush to disk via `IfcStore.SaveAs(path)` (write to temp, then swap).
5. On completion or **Cancel**, do a final save.

**Why not Revit's built-in exporter?**  
`Document.Export(IFCExportOptions)` is atomic — it cannot be paused, resumed, or limited to a subset.

---

### Change Tracking (Dual Map)

**Problem:** `GetDeletedElementIds()` returns `ElementId` only — deleted elements cannot be queried for their `UniqueId`.

**Solution — two maps maintained in sync:**

| Map              | Type                                            | Purpose                                                               |
| ---------------- | ----------------------------------------------- | --------------------------------------------------------------------- |
| `ExportStateMap` | `Dictionary<string(UniqueId), string(IfcGuid)>` | Primary map for IFC entity lookup. Serialised to JSON sidecar.        |
| `ElementIdIndex` | `Dictionary<ElementId, string(UniqueId)>`       | Reverse index for deletion events. In-memory only, rebuilt on resume. |

**Change processing priority:** Pending changes (from `DocumentChanged`) are processed before the initial export queue.

| Change Type  | Action                                                                                                              |
| ------------ | ------------------------------------------------------------------------------------------------------------------- |
| **Added**    | Export element → insert IFC entity → record in both maps                                                            |
| **Modified** | `ElementIdIndex` → `UniqueId` → `ExportStateMap` → `IfcGuid` → remove old IFC entity → re-export → update both maps |
| **Deleted**  | `ElementIdIndex` → `UniqueId` → `ExportStateMap` → `IfcGuid` → remove IFC entity → remove from both maps            |

**Sidecar file** (`<filename>.ifc.json`):

- Contains: `ExportStateMap`, IFC version, element count, last export timestamp, document path/title
- Saved on every periodic disk flush and on session end
- On resume: `ElementIdIndex` rebuilt by iterating live document elements and matching `UniqueId` against sidecar data

---

### Workshared Model Handling

**Reality check for remote changes:**  
Reload Latest and SWC lifecycle events do not provide direct per-element payloads. For this feature, `DocumentChanged` is treated as local-user only and must not be used to infer edits made by other users.

**Dedicated worksharing events** (on `ControlledApplication`):

| Event                              | Action                                                             |
| ---------------------------------- | ------------------------------------------------------------------ |
| `DocumentSynchronizingWithCentral` | Set `_syncInProgress = true`. Idling handler skips all processing. |
| `DocumentSynchronizedWithCentral`  | Clear `_syncInProgress`, reset quiet period timer.                 |
| `DocumentReloadedLatest`           | Clear `_syncInProgress`, reset quiet period timer.                 |

**Key behaviours:**

- Local-user changes found via `DocumentChanged` are processed one-by-one during subsequent idle ticks.
- Remote-user changes are discovered by periodic reconciliation (fingerprint comparison against live model state).
- If another user modifies an already-exported element, reconciliation re-exports it from the latest visible model state after Reload Latest/SWC.
- Element ownership/checkout is irrelevant — our export is read-only, and worksharing only restricts writing.
- Multiple users can target the same IFC file only because writes are serialized with a lock/lease; without locking, corruption risk is high.

---

### Shared IFC Lock/Lease Contract

Use a per-target lock file to serialize writes by all exporter clients.

**Lock file path:**

- `<target>.ifc.lock`

**Lock file format (JSON):**

- `ownerUser`: OS/Revit user identifier
- `ownerMachine`: machine name
- `ownerProcessId`: process ID
- `ownerSessionId`: exporter session GUID
- `createdUtc`: ISO timestamp
- `lastHeartbeatUtc`: ISO timestamp
- `leaseSeconds`: integer (start with 30)
- `targetIfcPath`: absolute path

**Acquire algorithm:**

- Attempt atomic create (`FileMode.CreateNew`) of lock file.
- If create succeeds, writer lease is acquired.
- If lock exists, read JSON and evaluate staleness: `nowUtc - lastHeartbeatUtc > leaseSeconds * 2`.
- If stale, attempt takeover using compare-then-replace semantics (write temp + atomic replace only if content hash still matches what was read).
- If not stale or takeover fails, do not write IFC this tick; reschedule save task.

**Heartbeat and renewal:**

- While holding lock, update `lastHeartbeatUtc` at a fixed interval (start with every 10 seconds).
- Heartbeat update must be atomic (temp file + replace).

**Release algorithm:**

- On successful save completion, cancel, or dispose: delete lock file only if current content still matches current owner/session.
- Never delete a lock owned by another session.

**Safety rules:**

- Max retries per save attempt chain (start with 12 retries, exponential backoff capped at 5 seconds).
- If lock cannot be acquired within retry budget, keep session running and report `Waiting for IFC write lock` in UI.
- IFC writes always go to temp IFC first, then atomic swap into target path, while lock is held.
- Sidecar update occurs in same critical section as IFC swap.

---

### Deferred Task Pump (Idling-Safe Pattern)

To keep `Idling` lightweight and deterministic, use a deferred task queue inspired by the article's design.

**Pattern:**

- `Idling` does not contain export logic directly; it only loops tasks and calls `Eval(uiApp)`.
- A task has: callback, awaited condition, payload, and `Completed` flag.
- Ready checks can be count-based, time-based, or context-based (e.g., document path/state).
- Use `for` iteration, not `foreach`, so callbacks can enqueue follow-up tasks safely.
- Completed tasks are removed immediately to avoid zombie work.

**Why this is useful here:**

- Keeps postable-command follow-ups and export/reconciliation steps composable.
- Avoids a monolithic idling method.
- Makes retry/backoff logic (e.g., lock unavailable) straightforward by rescheduling tasks.

**Guard rails for this pattern:**

- Each task must be short and idempotent.
- Each task sets `Completed = true` on terminal success/failure paths.
- Any retry path must have a max attempt count or timeout.

---

### Geometry Export

**Tessellation strategy (initial implementation):**

- `ViewDetailLevel.Fine` for all geometry extraction.
- `Element.get_Geometry(Options)` → iterate `GeometryObject` → extract `Solid` → `Solid.Faces` → `Face.Triangulate()` → vertex/triangle lists.
- Write as `IfcFacetedBrep` (IFC2x3) or `IfcTriangulatedFaceSet` (IFC4+).
- Skip elements with null or empty geometry.

**Element filtering:**

- `FilteredElementCollector`: all element instances (not types).
- Exclude element types, view-specific elements, annotations, detail items.
- Final filter: skip elements where `get_Geometry()` yields no `Solid` objects.

**Coordinate system:**

- Feet → metres: multiply all coordinates by `0.3048`.
- Map Revit internal origin to IFC project base point via `IfcSite` → `IfcBuilding` → `IfcBuildingStorey` placement chain.

**Minimal properties** — single `IfcPropertySet` ("Revit Identity"):

- Element Name
- Category name
- Family and Type name
- Revit ElementId (integer)
- Revit UniqueId (string)
- Level name (if applicable)

---

### IFC Geometry Reference

For context during implementation. The initial implementation uses only **Faceted BRep** and **Triangulated Face Set**.

| Representation          | IFC Concept                | Schema  | Used Initially?           |
| ----------------------- | -------------------------- | ------- | ------------------------- |
| Swept solid / extrusion | `IfcExtrudedAreaSolid`     | IFC2x3+ | No (future Option B)      |
| Faceted BRep            | `IfcFacetedBrep`           | IFC2x3+ | **Yes** (IFC2x3 target)   |
| Advanced BRep           | `IfcAdvancedBrep`          | IFC4+   | No                        |
| Triangulated face set   | `IfcTriangulatedFaceSet`   | IFC4+   | **Yes** (IFC4/4.3 target) |
| Clipping / CSG          | `IfcBooleanClippingResult` | IFC2x3+ | No                        |

---

## Architecture Summary

```
RevitApplication.OnStartup
  └ Register "IFC Export" ribbon button

IfcExportCommand (IExternalCommand)
  └ Opens / focuses IfcExportDialog (modeless WPF window)

IfcExportDialog (WPF, MVVM)
  └ IfcExportViewModel : BaseViewModel
        ├ IFC version selection
        ├ Destination folder picker
        ├ Progress (current / total, status message)
        ├ Run / Pause / Cancel commands (RelayCommand<T>)
        └ Bound to IdleExportOrchestrator state

IdleExportOrchestrator
  ├ Subscribes to UIApplication.Idling
  ├ Subscribes to ControlledApplication.DocumentChanged (local-user changes)
  ├ Subscribes to ControlledApplication.DocumentSynchronizingWithCentral
  ├ Subscribes to ControlledApplication.DocumentSynchronizedWithCentral
  ├ Subscribes to ControlledApplication.DocumentReloadedLatest
  ├ ExportQueue: ordered list of ElementIds to process
  ├ PendingChanges: queue of local add/modify/delete change events
  ├ ExportStateMap: Dictionary<UniqueId, IfcGuid>
  ├ ElementIdIndex: Dictionary<ElementId, UniqueId>
  ├ SyncInProgress flag
  └ Idling handler:
        1. Skip if SyncInProgress or quiet period not elapsed
        2. Process next item from PendingChanges (priority) or ExportQueue
        3. Delegate to IfcElementWriter
        4. Update ExportStateMap + ElementIdIndex
        5. Periodic save: IfcStore → temp file → swap + sidecar update

IfcElementWriter
  ├ ExtractRevitGeometry (get_Geometry → Solid → Face.Triangulate, Fine)
  ├ WriteIfcGeometry (IfcFacetedBrep / IfcTriangulatedFaceSet via Xbim)
  ├ WriteIfcProperties (minimal "Revit Identity" → IfcPropertySet)
  └ WriteIfcPlacement (Revit transform → IfcLocalPlacement, ft→m)
```

---

## Key External Dependencies

| Library         | NuGet Package               | Purpose                                               | Licence    |
| --------------- | --------------------------- | ----------------------------------------------------- | ---------- |
| Xbim.Essentials | `Xbim.Essentials`           | IFC entity model, file read/write, all IFC schemas    | CDDL       |
| Revit API       | (already referenced)        | Idling, DocumentChanged, worksharing events, geometry | Commercial |
| Newtonsoft.Json | (already likely referenced) | Serialise sidecar file                                | MIT        |

> **Xbim.Essentials** targets .NET Standard 2.0 — compatible with .NET Framework 4.8 (Revit 2021–2024) and .NET 8 (Revit 2025).
