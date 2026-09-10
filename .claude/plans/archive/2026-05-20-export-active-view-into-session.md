# Export Active View Into Live Session IFC — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an "Export Active View" button to the IFC Export dialog that updates the live session IFC in one synchronous batch — collecting visible elements, removing their old IFC representations, re-exporting with fresh geometry, and saving.

**Architecture:** New `ExportActiveView` request type flows through the existing `ExternalEvent` → `IfcExportRequestHandler` → `IdleExportOrchestrator` pipeline. The orchestrator adds an `IsReadyForViewExport` guard property and an `ExportActiveView(viewId, doc)` method that reuses `IfcElementWriter.RemoveElement` and `WriteElement`. Cleanup removes the now-redundant `IfcViewExportCommand`.

**Tech Stack:** C# / .NET 8 (Revit 2025 target), Revit API, Xbim.Ifc2x3. No automated test suite — all verification is manual inside a running Revit session. Build with `dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug`.

---

## File Map

| File | Action |
|---|---|
| `MepoverSharedProject/IfcExport/IfcExportRequestHandler.cs` | Modify — add enum value + callback field + Execute branch |
| `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs` | Modify — add `IsReadyForViewExport` + `ExportActiveView()` |
| `MepoverSharedProject/IfcExport/IfcExportViewModel.cs` | Modify — add `ExportViewCommand` + `CanExportView()` + `OnExportView()` |
| `MepoverSharedProject/IfcExport/IfcExportDialog.xaml` | Modify — add button |
| `MepoverSharedProject/IfcExport/IfcViewExportCommand.cs` | **Delete** |
| `MepoverSharedProject/RevitApplication.cs` | Modify — remove ribbon button |
| `MepoverSharedProject/MepoverSharedProject.projitems` | Modify — remove compile entry |

---

## Task 1: Extend IfcExportRequestHandler

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcExportRequestHandler.cs`

- [ ] **Step 1: Add enum value and callback field**

In `IfcExportRequestHandler.cs`, add `ExportActiveView` to the enum and add the callback field. The full file becomes:

```csharp
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
                    return;
                }

                if (req == IfcExportRequest.ExportActiveView)
                {
                    ElementId viewId = app.ActiveUIDocument?.ActiveView?.Id;
                    Document doc     = app.ActiveUIDocument?.Document;
                    if (viewId == null || doc == null)
                    {
                        ExportViewCallback?.Invoke("Error: No active view.");
                        return;
                    }
                    string result = Orchestrator?.ExportActiveView(viewId, doc)
                                    ?? "Error: Export session not initialised.";
                    ExportViewCallback?.Invoke(result);
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
                ExportViewCallback?.Invoke("Error: " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug 2>&1 | Select-String "error|Build succeeded|FAILED"
```
Expected: `Build succeeded.  0 Error(s)`

---

## Task 2: Add IsReadyForViewExport and ExportActiveView to Orchestrator

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Add `IsReadyForViewExport` property**

Add this property directly after the `State` property (after line `public ExportState State => _state;`):

```csharp
/// <summary>
/// True when the button should be enabled — session exists, initial export finished,
/// and the orchestrator is in a state where the Store is valid.
/// </summary>
public bool IsReadyForViewExport =>
    _session?.InitialExportComplete == true
    && _session?.Store != null
    && (_state == ExportState.Running || _state == ExportState.Paused);
```

- [ ] **Step 2: Add `ExportActiveView` method**

Add this method in the public API region, after the `Cancel()` method and before the `// Idling handler` comment block:

```csharp
/// <summary>
/// Synchronously updates the live session IFC with the elements visible in <paramref name="viewId"/>.
/// For each visible element: removes the existing IFC entity (if any) then re-exports with
/// fresh geometry. Saves the IFC file atomically after all elements are processed.
/// Must be called on the Revit main thread (inside an ExternalEvent handler).
/// </summary>
/// <returns>A status string suitable for display in the ViewModel's StatusText.</returns>
public string ExportActiveView(ElementId viewId, Document doc)
{
    if (!IsReadyForViewExport)
        return "Error: Initial export not yet complete. Wait until monitoring mode starts.";

    var elements = new FilteredElementCollector(doc, viewId)
        .WhereElementIsNotElementType()
        .ToElements();

    int updated = 0;
    int added   = 0;

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
    string target   = System.IO.Path.Combine(_session.DestinationFolder, safeName + ".ifc");
    string temp     = System.IO.Path.Combine(_session.DestinationFolder, safeName + "_partial.ifc");

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
```

Note: `ToElements()` returns an `IList<Element>`. The `using System.Collections.Generic;` import is already present in the file. `FilteredElementCollector` and `Document` are already imported.

- [ ] **Step 3: Build to verify no errors**

```
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug 2>&1 | Select-String "error|Build succeeded|FAILED"
```
Expected: `Build succeeded.  0 Error(s)`

---

## Task 3: Add ExportViewCommand to ViewModel

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcExportViewModel.cs`

- [ ] **Step 1: Add command declaration in the `#region Commands` block**

In the `#region Commands` block, add after `QuickExportCommand`:

```csharp
public RelayCommand<object> ExportViewCommand { get; }
```

- [ ] **Step 2: Wire the command in the constructor**

In the constructor body, add after the `QuickExportCommand =` line:

```csharp
ExportViewCommand = new RelayCommand<object>(p => CanExportView(), p => OnExportView());
```

- [ ] **Step 3: Add `CanExportView` and `OnExportView` methods**

Add these two methods after `OnQuickExport()`:

```csharp
private bool CanExportView() => _orchestrator?.IsReadyForViewExport == true;

private void OnExportView()
{
    StatusText = "Exporting active view…";
    _handler.ExportViewCallback = result =>
    {
        StatusText = result;
        ExportViewCommand.RaiseCanExecuteChanged();
    };
    _handler.Request(IfcExportRequest.ExportActiveView);
    _externalEvent.Raise();
}
```

- [ ] **Step 4: Add `ExportViewCommand.RaiseCanExecuteChanged()` to `RefreshCommands`**

The existing `RefreshCommands()` method is:

```csharp
private void RefreshCommands()
{
    RunCommand.RaiseCanExecuteChanged();
    PauseCommand.RaiseCanExecuteChanged();
    CancelCommand.RaiseCanExecuteChanged();
}
```

Replace it with:

```csharp
private void RefreshCommands()
{
    RunCommand.RaiseCanExecuteChanged();
    PauseCommand.RaiseCanExecuteChanged();
    CancelCommand.RaiseCanExecuteChanged();
    ExportViewCommand.RaiseCanExecuteChanged();
}
```

- [ ] **Step 5: Build to verify no errors**

```
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug 2>&1 | Select-String "error|Build succeeded|FAILED"
```
Expected: `Build succeeded.  0 Error(s)`

---

## Task 4: Add Button to IfcExportDialog.xaml

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcExportDialog.xaml`

- [ ] **Step 1: Add "Export Active View" button**

The Row 4 `StackPanel` currently contains only the Quick Export button:

```xml
<!-- Quick Export: bare-bones synchronous test, no task pump -->
<StackPanel Grid.Row="4" Orientation="Horizontal"
            HorizontalAlignment="Center" VerticalAlignment="Center"
            Margin="10,0,10,5">
    <Button Content="Quick Export (IFC2x3)" Command="{Binding QuickExportCommand}"
            Style="{StaticResource FlatButton}" Width="160" Margin="3,0"/>
</StackPanel>
```

Replace with:

```xml
<!-- Quick Export and Export Active View -->
<StackPanel Grid.Row="4" Orientation="Horizontal"
            HorizontalAlignment="Center" VerticalAlignment="Center"
            Margin="10,0,10,5">
    <Button Content="Quick Export (IFC2x3)" Command="{Binding QuickExportCommand}"
            Style="{StaticResource FlatButton}" Width="160" Margin="3,0"/>
    <Button Content="Export Active View" Command="{Binding ExportViewCommand}"
            Style="{StaticResource FlatButton}" Width="160" Margin="3,0"/>
</StackPanel>
```

- [ ] **Step 2: Build to verify no errors**

```
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug 2>&1 | Select-String "error|Build succeeded|FAILED"
```
Expected: `Build succeeded.  0 Error(s)`

---

## Task 5: Remove IfcViewExportCommand

**Files:**
- Delete: `MepoverSharedProject/IfcExport/IfcViewExportCommand.cs`
- Modify: `MepoverSharedProject/MepoverSharedProject.projitems`
- Modify: `MepoverSharedProject/RevitApplication.cs`

- [ ] **Step 1: Remove compile entry from projitems**

In `MepoverSharedProject.projitems`, remove this line:

```xml
<Compile Include="$(MSBuildThisFileDirectory)IfcExport\IfcViewExportCommand.cs" />
```

- [ ] **Step 2: Remove ribbon button from RevitApplication.cs**

In `RevitApplication.cs`, remove the entire "Active view export button" block:

```csharp
// Active view export button
PushButtonData viewExportData = new PushButtonData(
    "IFCViewExport",
    "Export\nView",
    thisAssemblyPath,
    "IfcExport.IfcViewExportCommand");
PushButton viewExportButton = ribbonPanel.AddItem(viewExportData) as PushButton;
viewExportButton.ToolTip = "Export elements visible in the active view to an IFC file";
```

- [ ] **Step 3: Delete the file**

Delete `MepoverSharedProject/IfcExport/IfcViewExportCommand.cs`.

- [ ] **Step 4: Build to verify no errors**

```
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug 2>&1 | Select-String "error|Build succeeded|FAILED"
```
Expected: `Build succeeded.  0 Error(s)`

---

## Task 6: Manual Verification in Revit

No automated test suite exists — all verification is manual inside a running Revit session.

- [ ] **Step 1: Deploy**

Build and let the post-build xcopy deploy to `%AppData%\Autodesk\Revit\Addins\2025\Mepover`.

- [ ] **Step 2: Verify button state is disabled before initial export completes**

1. Open a workshared Revit model
2. Click "IFC Export" ribbon button to open the dialog
3. Set destination folder, click "Run"
4. While initial export is in progress: confirm "Export Active View" button is greyed out

- [ ] **Step 3: Verify button enables after initial export finishes**

1. Wait until status shows "Monitoring for changes…"
2. Confirm "Export Active View" button is now enabled

- [ ] **Step 4: Verify view export updates the IFC**

1. Open a 3D view
2. Click "Export Active View"
3. Status text should update to show "View export complete — N updated, M added. Saved: …"
4. Open the saved IFC in a viewer (e.g. Solibri, BIMvision) and confirm only the view-visible elements appear refreshed

- [ ] **Step 5: Verify ribbon button is gone**

Confirm the "Export View" button no longer appears in the MEPover ribbon panel.

- [ ] **Step 6: Verify disabled states**

1. With session paused: button should still be enabled (Paused state is allowed)
2. With session cancelled: button should be disabled

---

## Self-Review Notes

- `ToElements()` returns `IList<Element>` — foreach works directly, no cast needed
- `MakeSafeFileName` is `private static` on `IdleExportOrchestrator` — accessible from `ExportActiveView` since it's in the same class
- Save path mirrors `EnqueueSaveTask` exactly (same `safeName + ".ifc"` pattern, same atomic swap)
- `PendingModifiedIds.Remove(elem.Id)` called before `WriteElement` — prevents dedup set from blocking future re-queuing after the view export
- `ExportViewCallback` invoked in the `catch` block so the UI is always unblocked even on exceptions
- Button enable condition covers both `Running` and `Paused` states — correct per design
- `RefreshCommands()` now calls `ExportViewCommand.RaiseCanExecuteChanged()` — fires on Run/Pause/Cancel, keeping button state in sync with session lifecycle
