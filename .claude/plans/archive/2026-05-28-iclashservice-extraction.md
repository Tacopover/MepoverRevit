# IClashService Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a Revit-free seam (`IClashService` + `ClashDto`) between the ClashDetector UI and the Revit API so the WPF UI and view logic can be developed, run, and unit-tested in a standalone host without launching Revit.

**Architecture:** Define a Revit-free interface `IClashService` and a plain `ClashDto`. The existing `RevitClashService` implements the interface (it keeps all Revit geometry code and bridges its `ExternalEvent` to an awaitable `Task`). `ClashDetectorViewModel` depends only on `IClashService` and stops creating/owning its `Window`. A new standalone `ClashDetector.DevHost` (net8 WPF exe) links the same Revit-free source files and runs the real window against a `MockClashService` that returns canned data. A net8 xUnit project tests the Revit-free pieces. The single source of truth stays in `MepoverSharedProject`; the host links the files, so no code is duplicated (unlike the abandoned `ClashDetectorUI` fork).

**Tech Stack:** C#, WPF, .NET 8.0-windows (host/tests) + .NET Framework 4.8 (plugin), MahApps.Metro.IconPacks 5.1.0, xUnit, Revit API (`ExternalEvent`), `TaskCompletionSource`.

---

## File Structure

**Created:**
- `MepoverSharedProject/ClashDetector/IClashService.cs` — Revit-free contract the UI depends on.
- `MepoverSharedProject/ClashDetector/ClashDto.cs` — Revit-free clash result (no `Element`/`Document` references).
- `ClashDetector.DevHost/ClashDetector.DevHost.csproj` — net8 WPF exe, links Revit-free source.
- `ClashDetector.DevHost/App.xaml` + `App.xaml.cs` — standalone entry, shows window with mock data.
- `ClashDetector.DevHost/MockClashService.cs` — fake `IClashService` with sample data.
- `ClashDetector.Tests/ClashDetector.Tests.csproj` — net8 xUnit project.
- `ClashDetector.Tests/ClashSettingsTests.cs`, `MockClashServiceTests.cs`, `ClashDetectorViewModelTests.cs`.

**Modified:**
- `MepoverSharedProject/MepoverSharedProject.projitems` — register the two new `.cs` files.
- `MepoverSharedProject/ClashDetector/RevitClashService.cs` — implement `IClashService`, add `RunClashesAsync`, map `Clash`→`ClashDto`, drop the pipe `SendMessage` call inside `RunClashes`.
- `MepoverSharedProject/ClashDetector/RequestHandler.cs` — route the handler to `ExecuteClashRun`.
- `MepoverSharedProject/ClashDetector/ViewModels/ClashDetectorViewModel.cs` — depend on `IClashService`, add `StatusMessage`, async run; remove window/`UIApplication` coupling.
- `MepoverSharedProject/ClashDetector/ClashDetectorCommand.cs` — own the window lifecycle (fixes the modal `ShowDialog` / `IsWindowClosed` reopen bug).
- `MepoverSharedProject/ClashDetector/ClashSettings.cs` — remove unused `using Autodesk.Revit.DB;`.
- `MepoverRevit.sln` — add the two new projects.

**Out of scope (do NOT build here):** a clash-results visualization grid, "zoom to element" navigation, removing the dead pipe/`RevitClashDetectorUI`/`ClashDetectorUI` experiments. Results surface only as a `StatusMessage` count string, which is enough to prove the seam end to end.

---

### Task 1: Revit-free contract (`IClashService` + `ClashDto`)

**Files:**
- Create: `MepoverSharedProject/ClashDetector/IClashService.cs`
- Create: `MepoverSharedProject/ClashDetector/ClashDto.cs`
- Modify: `MepoverSharedProject/MepoverSharedProject.projitems` (near line 29)

- [ ] **Step 1: Create `ClashDto.cs`**

```csharp
namespace ClashDetector
{
    public class ClashDto
    {
        public long ElementId1 { get; set; }
        public long ElementId2 { get; set; }
        public string Document1 { get; set; }
        public string Document2 { get; set; }
        public string TypeOfClash { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rotation { get; set; }
    }
}
```

- [ ] **Step 2: Create `IClashService.cs`**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClashDetector
{
    public interface IClashService
    {
        ClashSettings Settings { get; }
        Task<IReadOnlyList<ClashDto>> RunClashesAsync();
    }
}
```

- [ ] **Step 3: Register both files in the shared project**

Open `MepoverSharedProject/MepoverSharedProject.projitems`. Find the existing line (≈line 29):

```xml
    <Compile Include="$(MSBuildThisFileDirectory)ClashDetector\ClashSettings.cs" />
```

Add immediately after it:

```xml
    <Compile Include="$(MSBuildThisFileDirectory)ClashDetector\IClashService.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)ClashDetector\ClashDto.cs" />
```

- [ ] **Step 4: Build to verify the contract compiles**

Run: `dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug`
Expected: Build succeeded, 0 errors. (`ClashSettings` is already Revit-free enough to satisfy the `Settings` property type.)

- [ ] **Step 5: Commit**

```bash
git add MepoverSharedProject/ClashDetector/IClashService.cs MepoverSharedProject/ClashDetector/ClashDto.cs MepoverSharedProject/MepoverSharedProject.projitems
git commit -m "feat(clash): add Revit-free IClashService contract and ClashDto"
```

---

### Task 2: `RevitClashService` implements `IClashService`

This keeps every existing Revit geometry method untouched and adds an awaitable bridge: `RunClashesAsync` raises the existing `ExternalEvent`; the handler runs the geometry on Revit's main thread and completes a `TaskCompletionSource`.

**Files:**
- Modify: `MepoverSharedProject/ClashDetector/RevitClashService.cs`
- Modify: `MepoverSharedProject/ClashDetector/RequestHandler.cs`

- [ ] **Step 1: Declare the interface and add the async bridge fields/methods in `RevitClashService`**

Change the class declaration (line 20) from:

```csharp
    public class RevitClashService
```

to:

```csharp
    public class RevitClashService : IClashService
```

Add this field next to the other private fields (near line 28, by `RequestHandler handler;`):

```csharp
        private TaskCompletionSource<IReadOnlyList<ClashDto>> _runTcs;
```

Add these members (place them just above the existing `public void MakeRequest(RequestId request)` at line 609):

```csharp
        public Task<IReadOnlyList<ClashDto>> RunClashesAsync()
        {
            _runTcs = new TaskCompletionSource<IReadOnlyList<ClashDto>>();
            MakeRequest(RequestId.RunRevitClashes);
            return _runTcs.Task;
        }

        internal void ExecuteClashRun()
        {
            try
            {
                List<Clash> clashes = RunClashes();
                IReadOnlyList<ClashDto> dtos = clashes.Select(MapToDto).ToList();
                _runTcs?.TrySetResult(dtos);
            }
            catch (Exception ex)
            {
                _runTcs?.TrySetException(ex);
            }
        }

        private static ClashDto MapToDto(Clash clash)
        {
            return new ClashDto
            {
                ElementId1 = GetIdValue(clash.Element1.Id),
                ElementId2 = GetIdValue(clash.Element2.Id),
                Document1 = clash.Document1?.Title,
                Document2 = clash.Document2?.Title,
                TypeOfClash = clash.TypeOfClash,
                X = clash.RevitPoint?.X ?? 0,
                Y = clash.RevitPoint?.Y ?? 0,
                Z = clash.RevitPoint?.Z ?? 0,
                Rotation = clash.Rotation,
            };
        }

        private static long GetIdValue(ElementId id)
        {
#if REVIT2025
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
```

`System.Linq`, `System.Collections.Generic`, and `System.Threading.Tasks` are already imported at the top of the file.

- [ ] **Step 2: Stop `RunClashes` from popping the "Not connected to client" MessageBox**

In `RunClashes()` remove the now-pointless pipe call (line 338):

```csharp
            SendMessage(clashes.Count.ToString() + " clashes found");
            return clashes;
```

becomes:

```csharp
            return clashes;
```

(The named-pipe server is disabled, so `SendMessage` would otherwise show `MessageBox.Show("Not connected to client")` on every run. Count now flows to the UI via `RunClashesAsync` instead.)

- [ ] **Step 3: Route the external-event handler to `ExecuteClashRun`**

In `RequestHandler.cs`, change `RequestMethods.RunRevitAction` (lines 108-111) from:

```csharp
        public void RunRevitAction()
        {
            revitService.RunClashes();
        }
```

to:

```csharp
        public void RunRevitAction()
        {
            revitService.ExecuteClashRun();
        }
```

- [ ] **Step 4: Build to verify the Revit implementation compiles**

Run: `dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MepoverSharedProject/ClashDetector/RevitClashService.cs MepoverSharedProject/ClashDetector/RequestHandler.cs
git commit -m "feat(clash): implement IClashService on RevitClashService with awaitable run bridge"
```

---

### Task 3: Decouple `ClashDetectorViewModel` + fix window lifecycle in the command

The ViewModel currently takes the concrete `RevitClashService`, reads `revitService.UIApp.MainWindowHandle`, and creates/shows its own `Window` via modal `ShowDialog()` with broken `IsWindowClosed` bookkeeping. After this task the VM depends only on `IClashService`, holds no window, and exposes a `StatusMessage`. The command owns the window (created once, shown modeless, reactivated on reopen).

**Files:**
- Modify (rewrite): `MepoverSharedProject/ClashDetector/ViewModels/ClashDetectorViewModel.cs`
- Modify (rewrite): `MepoverSharedProject/ClashDetector/ClashDetectorCommand.cs`

- [ ] **Step 1: Rewrite `ClashDetectorViewModel.cs`**

```csharp
using MepoverSharedProject;
using System;
using System.Threading.Tasks;

namespace ClashDetector.ViewModels
{
    public class ClashDetectorViewModel : BaseViewModel
    {
        private readonly IClashService _clashService;

        private object _selectedViewModel;
        public object SelectedViewModel
        {
            get { return _selectedViewModel; }
            set
            {
                _selectedViewModel = value;
                OnPropertyChanged(nameof(SelectedViewModel));
            }
        }

        private string _selectedButton = "General";
        public string SelectedButton
        {
            get => _selectedButton;
            set
            {
                _selectedButton = value;
                OnPropertyChanged(nameof(SelectedButton));
            }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        private HostLinkViewModel _modelsViewModel;
        public HostLinkViewModel ModelsViewModel
        {
            get
            {
                if (_modelsViewModel == null)
                {
                    _modelsViewModel = new HostLinkViewModel(_clashService.Settings);
                }
                return _modelsViewModel;
            }
            set { _modelsViewModel = value; }
        }

        private CategoriesViewModel _categoryViewModel;
        public CategoriesViewModel CategoryViewModel
        {
            get
            {
                if (_categoryViewModel == null)
                {
                    _categoryViewModel = new CategoriesViewModel(_clashService.Settings);
                }
                return _categoryViewModel;
            }
            set { _categoryViewModel = value; }
        }

        private WorksetsViewModel _worksetViewModel;
        public WorksetsViewModel WorksetViewModel
        {
            get
            {
                if (_worksetViewModel == null)
                {
                    _worksetViewModel = new WorksetsViewModel();
                }
                return _worksetViewModel;
            }
            set { _worksetViewModel = value; }
        }

        public RelayCommand<object> NavigateToCommand { get; set; }
        public RelayCommand<object> RunCommand { get; set; }

        public ClashDetectorViewModel(IClashService clashService)
        {
            _clashService = clashService;
            SelectedViewModel = new CategoriesViewModel(clashService.Settings);

            NavigateToCommand = new RelayCommand<object>(p => true, p => NavigateTo(p));
            RunCommand = new RelayCommand<object>(p => true, async p => await RunClashesAsync());
        }

        private void NavigateTo(object parameter)
        {
            SelectedButton = parameter as string;
            switch (parameter)
            {
                case "Models":
                    SelectedViewModel = ModelsViewModel;
                    break;
                case "Categories":
                    SelectedViewModel = CategoryViewModel;
                    break;
                case "Worksets":
                    SelectedViewModel = WorksetViewModel;
                    break;
                default:
                    throw new ArgumentException("Invalid navigation target", nameof(parameter));
            }
        }

        public async Task RunClashesAsync()
        {
            StatusMessage = "Running clash detection...";
            var clashes = await _clashService.RunClashesAsync();
            StatusMessage = $"{clashes.Count} clashes found";
        }
    }
}
```

Note: `using UIFramework;`, `using System.Windows.Interop;`, `using ClashDetector.Views;`, and `using Autodesk.Revit.UI;` are intentionally gone — the VM is now Revit-free and window-free. `RunClashesAsync` is `public` so it is directly awaitable from unit tests.

- [ ] **Step 2: Rewrite `ClashDetectorCommand.cs` to own the window lifecycle**

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using ClashDetector.ViewModels;
using ClashDetector.Views;
using System;
using System.Windows;
using System.Windows.Interop;

namespace ClashDetector
{
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class ClashDetectorCommand : IExternalCommand
    {
        private static ClashDetectorWindow _window;
        private static RevitClashService _service;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                UIApplication uiApp = commandData.Application;
                _service = new RevitClashService(uiApp);
                _service.Initialize();

                var viewModel = new ClashDetectorViewModel(_service);
                _window = new ClashDetectorWindow { DataContext = viewModel };
                new WindowInteropHelper(_window).Owner = uiApp.MainWindowHandle;
                _window.Closed += (s, e) => _window = null;
                _window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetType().Name + " " + ex.Message);
                return Result.Failed;
            }
        }

        public static void CreatePanelButton(RibbonPanel ribbonPanel)
        {
            string thisAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            PushButtonData ccData = new PushButtonData("SC", "SheetCopier", thisAssemblyPath, typeof(ClashDetectorCommand).FullName);
            PushButton ccButton = ribbonPanel.AddItem(ccData) as PushButton;
            ccButton.ToolTip = "Start SheetCopier";
        }
    }
}
```

This replaces modal `ShowDialog()` with modeless `Show()` (correct for the `ExternalEvent` pattern) and fixes reopen: the static `_window` is nulled on close, so the next command invocation builds a fresh window, and an open window is reactivated via `Activate()`.

- [ ] **Step 3: Build to verify the decoupled UI compiles**

Run: `dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug`
Expected: Build succeeded, 0 errors. If the compiler reports the old `MakeRequest(RequestId.RunRevitClashes)` call in the VM is gone — that is expected; the VM now calls `_clashService.RunClashesAsync()` and the Revit service raises the event internally.

- [ ] **Step 4: Commit**

```bash
git add MepoverSharedProject/ClashDetector/ViewModels/ClashDetectorViewModel.cs MepoverSharedProject/ClashDetector/ClashDetectorCommand.cs
git commit -m "refactor(clash): ViewModel depends on IClashService; command owns window (fixes modal/reopen bug)"
```

---

### Task 4: Make `ClashSettings` strictly Revit-free

**Files:**
- Modify: `MepoverSharedProject/ClashDetector/ClashSettings.cs:1`

- [ ] **Step 1: Remove the unused Revit using directive**

Delete line 1:

```csharp
using Autodesk.Revit.DB;
```

(`ClashSettings` uses only `ObservableCollection<ListItem>` and has no Revit references. Removing the using makes the file safe to link into the non-Revit host.)

- [ ] **Step 2: Build to verify**

Run: `dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MepoverSharedProject/ClashDetector/ClashSettings.cs
git commit -m "chore(clash): drop unused Revit using from ClashSettings"
```

---

### Task 5: Standalone `ClashDetector.DevHost` (run the real UI without Revit)

A net8 WPF exe that links the Revit-free source files (single source of truth, no fork) and shows the real `ClashDetectorWindow` bound to a `MockClashService`.

**Files:**
- Create: `ClashDetector.DevHost/ClashDetector.DevHost.csproj`
- Create: `ClashDetector.DevHost/MockClashService.cs`
- Create: `ClashDetector.DevHost/App.xaml`
- Create: `ClashDetector.DevHost/App.xaml.cs`

- [ ] **Step 1: Create `ClashDetector.DevHost.csproj`**

The `Link` paths mirror the source folder layout so the window's relative resource reference `Source="..\..\Styles\StylesTotal.xaml"` (from `ClashDetector\Views\`) still resolves to `Styles\StylesTotal.xaml` inside this assembly.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>ClashDetector.DevHost</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MahApps.Metro.IconPacks" Version="5.1.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Revit-free MVVM base -->
    <Compile Include="..\MepoverSharedProject\BaseViewModel.cs" Link="Shared\BaseViewModel.cs" />
    <Compile Include="..\MepoverSharedProject\RelayCommand.cs" Link="Shared\RelayCommand.cs" />

    <!-- Contract + settings -->
    <Compile Include="..\MepoverSharedProject\ClashDetector\IClashService.cs" Link="ClashDetector\IClashService.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ClashDto.cs" Link="ClashDetector\ClashDto.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ClashSettings.cs" Link="ClashDetector\ClashSettings.cs" />

    <!-- ViewModels -->
    <Compile Include="..\MepoverSharedProject\ClashDetector\ViewModels\ListItem.cs" Link="ClashDetector\ViewModels\ListItem.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ViewModels\CategoriesViewModel.cs" Link="ClashDetector\ViewModels\CategoriesViewModel.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ViewModels\HostLinkViewModel.cs" Link="ClashDetector\ViewModels\HostLinkViewModel.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ViewModels\WorksetsViewModel.cs" Link="ClashDetector\ViewModels\WorksetsViewModel.cs" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\ViewModels\ClashDetectorViewModel.cs" Link="ClashDetector\ViewModels\ClashDetectorViewModel.cs" />

    <!-- Views (XAML + code-behind) -->
    <Page Include="..\MepoverSharedProject\ClashDetector\Views\ClashDetectorWindow.xaml" Link="ClashDetector\Views\ClashDetectorWindow.xaml" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\Views\ClashDetectorWindow.xaml.cs" Link="ClashDetector\Views\ClashDetectorWindow.xaml.cs" DependentUpon="ClashDetectorWindow.xaml" />
    <Page Include="..\MepoverSharedProject\ClashDetector\Views\CategoriesControl.xaml" Link="ClashDetector\Views\CategoriesControl.xaml" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\Views\CategoriesControl.xaml.cs" Link="ClashDetector\Views\CategoriesControl.xaml.cs" DependentUpon="CategoriesControl.xaml" />
    <Page Include="..\MepoverSharedProject\ClashDetector\Views\HostLinksControl.xaml" Link="ClashDetector\Views\HostLinksControl.xaml" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\Views\HostLinksControl.xaml.cs" Link="ClashDetector\Views\HostLinksControl.xaml.cs" DependentUpon="HostLinksControl.xaml" />
    <Page Include="..\MepoverSharedProject\ClashDetector\Views\WorksetsControl.xaml" Link="ClashDetector\Views\WorksetsControl.xaml" />
    <Compile Include="..\MepoverSharedProject\ClashDetector\Views\WorksetsControl.xaml.cs" Link="ClashDetector\Views\WorksetsControl.xaml.cs" DependentUpon="WorksetsControl.xaml" />

    <!-- Styles (self-contained: defines the VM->Control DataTemplates + SideBarButton) -->
    <Page Include="..\MepoverSharedProject\Styles\StylesTotal.xaml" Link="Styles\StylesTotal.xaml" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `MockClashService.cs`**

Seeds sample models so the Models tab shows rows, sample categories come from `ClashSettings`'s own constructor, and `RunClashesAsync` returns two canned clashes.

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using ClashDetector;
using ClashDetector.ViewModels;

namespace ClashDetector.DevHost
{
    public class MockClashService : IClashService
    {
        public ClashSettings Settings { get; }

        public MockClashService()
        {
            Settings = new ClashSettings();
            Settings.RevitModels1.Add(new ListItem("Host.rvt", true));
            Settings.RevitModels1.Add(new ListItem("Structure.rvt"));
            Settings.RevitModels2.Add(new ListItem("Host.rvt"));
            Settings.RevitModels2.Add(new ListItem("Structure.rvt", true));
        }

        public Task<IReadOnlyList<ClashDto>> RunClashesAsync()
        {
            IReadOnlyList<ClashDto> sample = new List<ClashDto>
            {
                new ClashDto { ElementId1 = 1001, ElementId2 = 2001, Document1 = "Host.rvt", Document2 = "Structure.rvt", TypeOfClash = "Pipes", X = 1, Y = 2, Z = 3 },
                new ClashDto { ElementId1 = 1002, ElementId2 = 2002, Document1 = "Host.rvt", Document2 = "Structure.rvt", TypeOfClash = "Ducts", X = 4, Y = 5, Z = 6 },
            };
            return Task.FromResult(sample);
        }
    }
}
```

- [ ] **Step 3: Create `App.xaml`**

No `StartupUri` — the window is created in code so the mock can be injected as `DataContext`.

```xml
<Application x:Class="ClashDetector.DevHost.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources />
</Application>
```

- [ ] **Step 4: Create `App.xaml.cs`**

```csharp
using System.Windows;
using ClashDetector.ViewModels;
using ClashDetector.Views;

namespace ClashDetector.DevHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var viewModel = new ClashDetectorViewModel(new MockClashService());
            var window = new ClashDetectorWindow { DataContext = viewModel };
            window.Show();
        }
    }
}
```

- [ ] **Step 5: Build and run the standalone host**

Run: `dotnet run --project ClashDetector.DevHost\ClashDetector.DevHost.csproj`
Expected: Build succeeds; the Clash Detector window opens in well under a second with **no Revit running**. Verify manually:
- The Categories tab shows the two category lists with the default checkboxes (e.g. Pipes/Ducts/Walls pre-checked).
- Clicking **Models** shows `Host.rvt` / `Structure.rvt`; clicking **Worksets** switches the panel without error.
- Clicking **Clash** does not throw (status will be wired in Task 6's behavior; the click invokes `MockClashService`).

If a `ResourceDictionary`/style load error appears, confirm `StylesTotal.xaml` is linked and that its relative path resolves from `ClashDetector\Views\`. (`StylesTotal.xaml` has no `MergedDictionaries`, so no other style file is required for this window.)

- [ ] **Step 6: Commit**

```bash
git add ClashDetector.DevHost
git commit -m "feat(clash): add standalone DevHost to run the UI without Revit via MockClashService"
```

---

### Task 6: Unit tests for the Revit-free pieces (TDD)

This is the payoff: real tests that run without Revit. The test project references `ClashDetector.DevHost`, which exposes the linked ViewModels, `ClashSettings`, and `MockClashService`.

**Files:**
- Create: `ClashDetector.Tests/ClashDetector.Tests.csproj`
- Create: `ClashDetector.Tests/MockClashServiceTests.cs`
- Create: `ClashDetector.Tests/ClashSettingsTests.cs`
- Create: `ClashDetector.Tests/ClashDetectorViewModelTests.cs`

- [ ] **Step 1: Create `ClashDetector.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ClashDetector.DevHost\ClashDetector.DevHost.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests**

`MockClashServiceTests.cs`:

```csharp
using System.Threading.Tasks;
using ClashDetector;
using ClashDetector.DevHost;
using Xunit;

namespace ClashDetector.Tests
{
    public class MockClashServiceTests
    {
        [Fact]
        public async Task RunClashesAsync_returns_two_sample_clashes()
        {
            var service = new MockClashService();
            var clashes = await service.RunClashesAsync();
            Assert.Equal(2, clashes.Count);
        }

        [Fact]
        public void Settings_exposes_seeded_models()
        {
            var service = new MockClashService();
            Assert.Equal(2, service.Settings.RevitModels1.Count);
        }
    }
}
```

`ClashSettingsTests.cs`:

```csharp
using System.Linq;
using ClashDetector;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashSettingsTests
    {
        [Fact]
        public void Default_categories_include_preselected_pipes()
        {
            var settings = new ClashSettings();
            var pipes = settings.Categories1.Single(c => c.Name == "Pipes");
            Assert.True(pipes.IsSelected);
        }
    }
}
```

`ClashDetectorViewModelTests.cs`:

```csharp
using System.Threading.Tasks;
using ClashDetector.DevHost;
using ClashDetector.ViewModels;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashDetectorViewModelTests
    {
        private static ClashDetectorViewModel CreateViewModel()
        {
            return new ClashDetectorViewModel(new MockClashService());
        }

        [Fact]
        public void Constructor_starts_on_categories_view()
        {
            var vm = CreateViewModel();
            Assert.IsType<CategoriesViewModel>(vm.SelectedViewModel);
        }

        [Fact]
        public void NavigateTo_models_switches_to_host_link_view()
        {
            var vm = CreateViewModel();
            vm.NavigateToCommand.Execute("Models");
            Assert.IsType<HostLinkViewModel>(vm.SelectedViewModel);
        }

        [Fact]
        public async Task RunClashesAsync_sets_status_with_clash_count()
        {
            var vm = CreateViewModel();
            await vm.RunClashesAsync();
            Assert.Equal("2 clashes found", vm.StatusMessage);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail first (project not yet in solution / fresh build)**

Run: `dotnet test ClashDetector.Tests\ClashDetector.Tests.csproj`
Expected on first run before the DevHost reference resolves or if any signature is wrong: FAIL. Once the project references resolve, all five tests should pass because the implementation from Tasks 2-5 already satisfies them. If `RunClashesAsync_sets_status_with_clash_count` fails, confirm the VM's `RunClashesAsync` is `public` and sets `StatusMessage` (Task 3, Step 1).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ClashDetector.Tests\ClashDetector.Tests.csproj`
Expected: Passed! - 5 tests.

- [ ] **Step 5: Commit**

```bash
git add ClashDetector.Tests
git commit -m "test(clash): add Revit-free unit tests for settings, mock service, and view model"
```

---

### Task 7: Wire new projects into the solution and verify end to end

**Files:**
- Modify: `MepoverRevit.sln`

- [ ] **Step 1: Add both projects to the solution**

Run:

```bash
dotnet sln MepoverRevit.sln add ClashDetector.DevHost\ClashDetector.DevHost.csproj
dotnet sln MepoverRevit.sln add ClashDetector.Tests\ClashDetector.Tests.csproj
```

- [ ] **Step 2: Build the plugin, build+run the host, run the tests**

Run each and confirm:

```bash
dotnet build MepoverRevit.2025\MepoverRevit.2025.csproj -c Debug
dotnet test ClashDetector.Tests\ClashDetector.Tests.csproj
dotnet run --project ClashDetector.DevHost\ClashDetector.DevHost.csproj
```

Expected: plugin build succeeded (0 errors); 5 tests pass; host window opens without Revit. Per `CLAUDE.md`, a green MepoverRevit.2025 build implies the 2021-2024 projects build too.

- [ ] **Step 3: Manual Revit smoke test (geometry path)**

Because the clash geometry only runs inside Revit, do one manual run: build a Revit-version project, launch Revit, open a model with a loaded link, run the ClashDetector command, click **Clash**, and confirm the window shows "`N` clashes found" and reopening the command after closing the window works (reactivates / rebuilds without the old `IsWindowClosed` freeze).

- [ ] **Step 4: Commit**

```bash
git add MepoverRevit.sln
git commit -m "build(clash): add DevHost and Tests projects to the solution"
```

---

## Follow-ups (not in this plan)

- Delete the dead UI experiments once the seam is trusted: the named-pipe code in `RevitClashService` (`StartServer`/`SendMessage`/`ListenForMessages`/`ClosePipe`), `NamedPipeHelper.cs`, the `RevitClashDetectorUI` project, and the forked `ClashDetectorUI` project.
- Build a real clash-results view (grid bound to `IReadOnlyList<ClashDto>`) plus a "zoom to element" service method (Revit-only impl; mock no-ops).
- Consider extracting the linked files into a multi-target (`net48;net8.0-windows`) class library if the `Link`-list maintenance becomes annoying.

## Self-Review notes
- **Spec coverage:** interface seam (T1), Revit impl with awaitable bridge (T2), Revit-free VM (T3), settings cleanup (T4), standalone runnable host (T5), tests without Revit (T6), wiring + verify (T7). All goal elements covered.
- **Type consistency:** `IClashService.RunClashesAsync()` returns `Task<IReadOnlyList<ClashDto>>` everywhere (interface, `RevitClashService`, `MockClashService`, VM, tests). `ClashSettings Settings { get; }` matches across interface and both impls. VM ctor signature `ClashDetectorViewModel(IClashService)` is used identically in the command, `App.xaml.cs`, and tests.
- **Cross-version:** `GetIdValue` guards `ElementId.Value` (2025/.NET8) vs `ElementId.IntegerValue` (2021-2024/.NET FW) with `#if REVIT2025`.
