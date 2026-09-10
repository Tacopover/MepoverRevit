# IFC Export Auto-Resume Design

**Date:** 2026-05-20  
**Status:** Approved

## Context

The IFC exporter runs during Revit idle time and produces an up-to-date `.ifc` file. When the user closes and reopens Revit, all state is lost — they must manually re-enable the exporter for each document. This spec describes how to persist the enabled state per document and silently auto-resume when a previously-enabled document is opened.

---

## Architecture

### New file: `IfcExportPersistenceService.cs`

Owns all JSON read/write for the per-document settings store. Lives in `MepoverSharedProject/IfcExport/`.

**Storage path:** `%AppData%\Mepover\ifc-export-settings.json`

**JSON structure:**
```json
{
  "My Project": {
    "destinationFolder": "C:\\Export\\IFC",
    "ifcVersion": "IFC4",
    "psetMappingFilePath": ""
  }
}
```

Key is `Document.Title`. Presence in the JSON means enabled; absence means disabled.

**Public API:**
- `SaveDocument(string title, string folder, string ifcVersion, string psetPath)` — upserts entry
- `RemoveDocument(string title)` — removes entry (export cancelled)
- `bool TryGetSettings(string title, out IfcExportDocumentSettings settings)` — lookup

**`IfcExportDocumentSettings` record** (same file):
```csharp
public record IfcExportDocumentSettings(string DestinationFolder, string IfcVersion, string PsetMappingFilePath);
```

File is created on first save. JSON parse errors are swallowed; corrupt file treated as empty.

---

## Auto-Start Flow

### 1. `RevitApplication.OnStartup()`

Calls `IfcExportCommand.InitializeForAutoStart(application, new IfcExportPersistenceService())`.

### 2. `IfcExportCommand.InitializeForAutoStart()`

- Creates `_handler` and `_externalEvent` once (valid API context in OnStartup)
- Stores `_persistence` reference
- Subscribes `application.ControlledApplication.DocumentOpened += OnDocumentOpenedForAutoStart`

### 3. `OnDocumentOpenedForAutoStart()` (static, in IfcExportCommand)

```
title = e.Document.Title
if not in persistence → return (no-op)
if _handler.Orchestrator is Running or Paused → return (don't interrupt active export)
uiApp = new UIApplication((Application)sender)
_viewModel = new IfcExportViewModel(uiApp, _handler, _externalEvent, _persistence)
_viewModel.ApplySettings(settings)   ← sets folder/version/pset without triggering save
_viewModel.TriggerStart()            ← raises ExternalEvent with Request.Start
```

No window is shown. Export begins on the next Idling tick.

### 4. User opens IFC Export panel

`IfcExportCommand.Execute()` sees `_viewModel != null`, calls `_viewModel.ShowWindow()`. User sees the export already running.

---

## Modified: `IfcExportCommand`

- Change `_handler`, `_externalEvent`, `_viewModel` from `private static` → `internal static`
- Add `internal static IfcExportPersistenceService _persistence`
- Add `internal static void InitializeForAutoStart(UIControlledApplication app, IfcExportPersistenceService persistence)`
- Add `private static void OnDocumentOpenedForAutoStart(object sender, DocumentOpenedEventArgs e)`
- Fix `Execute()`: replace `if (_viewModel == null || _viewModel.IsWindowClosed)` block so that if `_viewModel` is not null (auto-started), `ShowWindow()` is called on the existing instance instead of creating a new one:

```csharp
if (_viewModel == null)
{
    if (_handler == null)  // fallback if InitializeForAutoStart was not called
    {
        _handler = new IfcExportRequestHandler();
        _externalEvent = ExternalEvent.Create(_handler);
    }
    _viewModel = new IfcExportViewModel(uiApp, _handler, _externalEvent, _persistence);
}
_viewModel.ShowWindow();
```

---

## Modified: `IfcExportViewModel`

**Constructor change:** accepts optional `IfcExportPersistenceService persistence = null`

**New methods:**
- `internal void ApplySettings(IfcExportDocumentSettings s)` — sets the three setting properties directly (bypasses persistence save)
- `internal void TriggerStart()` — calls `OnRun()` (or directly raises the ExternalEvent with Start)

**Save triggers:**
- `OnRun()` → `_persistence?.SaveDocument(ActiveDocTitle, DestinationFolder, SelectedIfcVersion, PsetMappingFilePath)`
- `OnCancel()` → `_persistence?.RemoveDocument(ActiveDocTitle)`
- Property setters for `DestinationFolder`, `SelectedIfcVersion`, `PsetMappingFilePath` → if orchestrator is `Running`, update the saved entry

**Helper:**
```csharp
private string ActiveDocTitle => _uiApp.ActiveUIDocument?.Document?.Title ?? string.Empty;
```

---

## Modified: `RevitApplication`

Add one line in `OnStartup()` after `AddRibbonPanel()`:
```csharp
IfcExportCommand.InitializeForAutoStart(application, new IfcExportPersistenceService());
```

Add `using IfcExport;` directive.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Another export already running when document opens | Skip auto-start (don't interrupt) |
| DestinationFolder missing/empty | `OnRun()` guards already reject this |
| JSON file missing | Created on first save; `TryGetSettings` returns false |
| JSON corrupt | Swallow parse error, treat as empty; overwrite on next save |
| Document title collision (two models same name) | Both share settings — acceptable given user chose title-based identity |
| User renames model | Old title entry orphaned in JSON; harmless, cleaned on next `RemoveDocument` for that title |

---

## Files Changed

| Action | File |
|---|---|
| Create | `MepoverSharedProject/IfcExport/IfcExportPersistenceService.cs` |
| Modify | `MepoverSharedProject/IfcExport/IfcExportCommand.cs` |
| Modify | `MepoverSharedProject/IfcExport/IfcExportViewModel.cs` |
| Modify | `MepoverSharedProject/RevitApplication.cs` |

---

## Verification

1. **Happy path:** Enable export for a project → close Revit → reopen → IFC export should start automatically with the same settings.
2. **Cancel removes persistence:** Start export → cancel → close Revit → reopen → export should NOT auto-start.
3. **Settings restored:** Verify DestinationFolder/IFC version/pset file shown correctly when user opens IFC Export panel after auto-start.
4. **No duplicate window:** Auto-started export + user clicks IFC Export button → only one window opens.
5. **Active export not interrupted:** Export running for document A → user opens document B (previously enabled) → document A export continues undisturbed.
6. **JSON file location:** Verify `%AppData%\Mepover\ifc-export-settings.json` created and populated correctly.
