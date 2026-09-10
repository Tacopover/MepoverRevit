# Design: Export Active View Into Live Session IFC

**Date:** 2026-05-20
**Branch:** fix/ifc-related-elements-xbim-net8

---

## Summary

Add an "Export Active View" button to `IfcExportDialog` that updates the live session IFC file in one synchronous batch: collects elements visible in the active Revit view, removes their old IFC representations if they already exist, re-exports them with fresh geometry, and saves the file. Remove the now-redundant `IfcViewExportCommand` (which created a separate IFC from scratch).

---

## Architecture

### Files changed

| File | Change |
|---|---|
| `IfcExportRequestHandler.cs` | Add `ExportActiveView` enum value; add `ExportViewCallback` field; handle in `Execute()` |
| `IdleExportOrchestrator.cs` | Add `IsReadyForViewExport` property; add `ExportActiveView(viewId, doc)` method |
| `IfcExportViewModel.cs` | Add `ExportViewCommand`; add `CanExportView()`; wire callback; include in `RefreshCommands()` |
| `IfcExportDialog.xaml` | Add "Export Active View" button in Row 4 StackPanel alongside Quick Export |
| `IfcViewExportCommand.cs` | **Deleted** |
| `RevitApplication.cs` | Remove "Export View" ribbon button |
| `MepoverSharedProject.projitems` | Remove `IfcViewExportCommand.cs` compile entry |

### Files unchanged

- `IfcElementWriter.cs` — `WriteElement` and `RemoveElement` already implement the required operations
- `IfcExportSession.cs` — `ElementIdIndex`, `ExportStateMap`, `Store` already hold everything needed

---

## Data Flow

```
[WPF thread] ExportViewCommand.Execute()
  → StatusText = "Exporting active view…"
  → handler.ExportViewCallback = result => { StatusText = result; }
  → handler.Request(ExportActiveView)
  → _externalEvent.Raise()

[Revit main thread] IfcExportRequestHandler.Execute(app)
  → viewId = app.ActiveUIDocument.ActiveView.Id
  → doc    = app.ActiveUIDocument.Document
  → Orchestrator.ExportActiveView(viewId, doc)
  → ExportViewCallback(statusString)

[still Revit main thread] IdleExportOrchestrator.ExportActiveView(viewId, doc)
  1. Guard: !IsReadyForViewExport → return "Error: …"
  2. FilteredElementCollector(doc, viewId).WhereElementIsNotElementType() → elements
  3. For each element:
       a. ElementIdIndex.TryGetValue(id, out uniqueId)
            if found → ExportStateMap.TryGetValue(uniqueId, out ifcGuid)
                      → IfcElementWriter.RemoveElement(ifcGuid, session)
                      → session.ExportStateMap.Remove(uniqueId)
                      → session.ElementIdIndex.Remove(id)
       b. IfcElementWriter.WriteElement(element, session)
            (updates ExportStateMap + ElementIdIndex on success)
  4. session.Store.SaveAs(outputPath, StorageType.Ifc)
  5. Re-baseline session.CentralFileTimestampAtLastSave (same as EnqueueSaveTask)
  6. Return "Exported N element(s) to <path>" or "Error: …"
```

---

## Key Properties and Guards

### `IsReadyForViewExport` (on `IdleExportOrchestrator`)

```csharp
public bool IsReadyForViewExport =>
    _session?.InitialExportComplete == true
    && _session?.Store != null
    && (_state == ExportState.Running || _state == ExportState.Paused);
```

Button is disabled until the initial full export completes.

### `CanExportView()` (on `IfcExportViewModel`)

```csharp
private bool CanExportView() => _orchestrator?.IsReadyForViewExport == true;
```

---

## IfcExportRequestHandler Changes

```csharp
// New enum value
ExportActiveView

// New fields
public Action<string> ExportViewCallback { get; set; }

// In Execute():
if (req == IfcExportRequest.ExportActiveView)
{
    ElementId viewId = app.ActiveUIDocument?.ActiveView?.Id;
    Document doc     = app.ActiveUIDocument?.Document;
    if (viewId == null || doc == null)
    {
        ExportViewCallback?.Invoke("Error: No active view.");
        return;
    }
    string result = Orchestrator.ExportActiveView(viewId, doc);
    ExportViewCallback?.Invoke(result);
    return;
}
```

---

## XAML Change

Row 4 `StackPanel` in `IfcExportDialog.xaml` gains a second button:

```xml
<Button Content="Export Active View" Command="{Binding ExportViewCommand}"
        Style="{StaticResource FlatButton}" Width="160" Margin="3,0"/>
```

Both buttons remain visible at all times. "Quick Export" is always enabled (creates a standalone IFC). "Export Active View" is only enabled once the session's initial export is complete.

---

## Cleanup

- `IfcViewExportCommand.cs` is deleted. It created a standalone IFC from scratch and handled its own file rename logic — both now unnecessary.
- The "Export View" ribbon button in `RevitApplication.cs` is removed. Users access the feature through the dialog.
- The "WS Change Test" ribbon button and `WsChangeTestCommand.cs` are **not** removed — they are still needed for worksharing testing.

---

## Error Handling

| Condition | Behaviour |
|---|---|
| Session not ready (`IsReadyForViewExport = false`) | Button disabled — cannot reach handler |
| No active view at execute time | Callback returns `"Error: No active view."` → StatusText |
| View type has no 3-D geometry (schedule, legend, etc.) | `FilteredElementCollector(doc, viewId)` returns 0 elements; status shows `"Exported 0 elements"` — no error, file unchanged |
| Individual element geometry fails | `WriteElement` catches and skips (existing behaviour); count excludes skipped |
| Save fails | Exception caught; callback returns `"Error: …"` |

---

## Out of Scope

- Progress bar per-element during view export (synchronous batch — no per-tick updates)
- Filtering by view type before export (collector naturally returns nothing for non-3-D views)
- Undo support
