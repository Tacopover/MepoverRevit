# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Knowledge base - read first

### Exploration Protocol (follow this order — every time)

**Step 1 — Use semble for code location (never Read/Grep for discovery).** Use `mcp__semble__search` and `mcp__semble__find_related` to locate code. Always pass the project root as `repo` path. Include this ignore list:

```
ignore: [".git", "bin", "obj", ".vs", "packages", "Packages", "Memory", ".semble-test"]
```

**Do NOT use Read, Grep, or Glob for discovery.** Semble returns precise file:line references in one call. Only call Read when you have a specific file path and need full content. Only call Grep for exact string matches that can't be expressed as a natural-language query.

**Step 2 — Delegate broad exploration to sub-agents.** When a task requires mapping multiple files, tracing View/ViewModel chains, or understanding subsystem relationships — spawn a `cavecrew-investigator` sub-agent. Its output is caveman-compressed (~60% fewer tokens injected into main context). Use it for any "find all X", "map this subsystem", or "what calls Y" questions rather than doing multi-file reads inline.

## Build & Deploy

Build with Visual Studio or MSBuild:

```
dotnet build MepoverRevit.sln
```

Each version-specific project (2021–2025) compiles to `../bin/{Configuration}/{RevitVersion}/`. Post-build events automatically xcopy the DLL, XML config, and `.addin` manifest to `%AppData%\Autodesk\Revit\Addins\{Version}\Mepover`.

ClashDetector has Revit-free unit tests: `dotnet test ClashDetector.Tests/ClashDetector.Tests.csproj`. All other testing is manual inside a running Revit session.

## Multi-Version Shared Project Architecture

All plugin logic lives in **MepoverSharedProject** (a `.projitems` shared items project). Five thin wrapper projects (`MepoverRevit.2021`–`MepoverRevit.2025`) import it and define version constants (`REVIT2021`, `REVIT2024`, etc.). Revit 2025 targets `.NET 8.0-windows`; earlier versions target `.NET Framework 4.8`.

Use `#if REVIT2025` guards for version-specific API divergences. Never put logic in the wrapper projects — only the shared project.

## Revit Plugin Patterns

**Entry point:** `RevitApplication.cs` implements `IExternalApplication`, runs on Revit startup, builds the ribbon, and wires `UIApplication.Idling`.

**Modeless WPF dialogs** (SheetCopier, ClashDetector pattern):

- UI lives on a non-Revit thread; Revit API may only be called on the main thread.
- Use `ExternalEvent` + a `RequestHandler` to queue operations from the UI, then execute them inside the event callback.
- Parent dialogs to Revit's main window via `WindowInteropHelper.Owner`.
- Keep a static ViewModel so the dialog can be reopened without losing state.

**Idle-time task pump** (for long-running operations):

- Uses `UIApplication.Idling` event to queue and execute work in small increments.
- Runs one task per idle tick to keep Revit responsive.
- All Revit API access remains on the main thread inside the idle callback.

## Projects

**MepoverSharedProject** contains three plugins:

- SheetCopier — copies sheets from linked Revit files to the host model
- ClashDetector — detects clashes between linked models
- IfcExport — exports 3D geometry to IFC format during Revit idle time

**ClashDetector.DevHost** — standalone .NET 8 WPF exe for UI development without Revit. Links Revit-free files from `MepoverSharedProject` via `<Link>` (no copies — single source of truth). Uses `MockClashService`. Edit views/styles in `MepoverSharedProject` only; both projects pick up changes on next build.

**ClashDetector.Tests** — xUnit tests (net8.0-windows), references `ClashDetector.DevHost`. No Revit required.

## Build Verification

**Build verification is mandatory after every code change.** Fix all errors before considering a task complete. When a task is completed, display a list of modified, added, and deleted files that the user can click to navigate in the IDE. Build the MepoverRevit.2025 for this purpose as this is a .NET8 project and the other ones are .NET Framework project. So only the .NET8 project can be built with the dotnet CLI. If the MepoverRevit.2025 build correctly then we can assume that the other projects build correctly as well.

## MVVM Base Classes

`BaseViewModel` (implements `INotifyPropertyChanged`) and `RelayCommand<T>` are the MVVM foundation for all WPF views. Follow this pattern for any new UI.

## Dependencies

- `Revit_All_Main_Versions_API_x64` NuGet — provides Revit API per version
- `Xbim.Essentials` — IFC file read/write (v6.0.489 for 2021–2024, v6.0.461 for 2025)
- `MahApps.Metro.IconPacks` — UI icons
