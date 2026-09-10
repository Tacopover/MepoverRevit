---
name: revit-external-event
description: Use when adding a new modeless WPF dialog or background operation in MepoverRevit that needs to call the Revit API from a non-Revit thread.
---

## The constraint

Revit API calls are only valid on the **main Revit thread**. Modeless WPF dialogs run on a separate thread. Calling the Revit API directly from a dialog handler will crash Revit.

The solution: `ExternalEvent` + `IExternalEventHandler`. The UI queues a request; Revit executes it on the main thread at the next idle tick.

## Minimal scaffold (3 files)

### 1 — Request enum + handler

```csharp
// MepoverSharedProject/MyFeature/MyRequestHandler.cs
public enum MyRequest { None, DoSomething, DoSomethingElse }

public class MyRequestHandler : IExternalEventHandler
{
    public MyRequest Request { get; set; } = MyRequest.None;

    // Data passed from UI to handler (set before Raise())
    public string InputData { get; set; }

    public void Execute(UIApplication app)
    {
        switch (Request)
        {
            case MyRequest.DoSomething:
                var doc = app.ActiveUIDocument.Document;
                using (var tx = new Transaction(doc, "My Operation"))
                {
                    tx.Start();
                    // Revit API calls here — safe on main thread
                    tx.Commit();
                }
                break;
        }
        Request = MyRequest.None; // always reset
    }

    public string GetName() => "MyRequestHandler";
}
```

### 2 — ViewModel (static, retained across dialog open/close)

```csharp
// MepoverSharedProject/MyFeature/MyViewModel.cs
public class MyViewModel : BaseViewModel
{
    private readonly ExternalEvent _externalEvent;
    private readonly MyRequestHandler _handler;

    public MyViewModel(ExternalEvent externalEvent, MyRequestHandler handler)
    {
        _externalEvent = externalEvent;
        _handler = handler;
        DoSomethingCommand = new RelayCommand<object>(_ => OnDoSomething());
    }

    public RelayCommand<object> DoSomethingCommand { get; }

    private void OnDoSomething()
    {
        _handler.InputData = "some value";
        _handler.Request = MyRequest.DoSomething;
        _externalEvent.Raise(); // returns immediately — Revit executes async
    }
}
```

### 3 — Dialog window

```csharp
// MepoverSharedProject/MyFeature/MyDialog.xaml.cs
public partial class MyDialog : Window
{
    public MyDialog(MyViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

## Wiring in RevitApplication.cs

```csharp
// Initialize once at startup (OnStartup)
private static MyRequestHandler _myHandler;
private static ExternalEvent _myExternalEvent;
private static MyViewModel _myViewModel;  // static — survives dialog close
private static MyDialog _myDialog;

// In OnStartup:
_myHandler = new MyRequestHandler();
_myExternalEvent = ExternalEvent.Create(_myHandler);
_myViewModel = new MyViewModel(_myExternalEvent, _myHandler);

// Ribbon button command:
private void OpenMyDialog(object sender, RoutedEventArgs e)
{
    if (_myDialog == null || !_myDialog.IsLoaded)
    {
        _myDialog = new MyDialog(_myViewModel);
        // Parent to Revit main window
        new WindowInteropHelper(_myDialog).Owner =
            Process.GetCurrentProcess().MainWindowHandle;
        _myDialog.Show(); // modeless — not ShowDialog()
    }
    else
    {
        _myDialog.Activate();
    }
}
```

## Rules

- `ExternalEvent.Raise()` is fire-and-forget — it returns before Revit executes the handler. Never await it or poll for completion.
- Set all handler input data **before** calling `Raise()` — the handler reads them on the main thread.
- Reset `Request` to `None` at the end of `Execute()` to prevent double-execution.
- Keep ViewModel **static** — recreating it on each dialog open loses state (selections, results).
- Use `Show()` not `ShowDialog()` — modeless dialogs must not block the Revit thread.
- All Revit API calls go inside a `Transaction`. Never modify the document outside one.
- Put logic only in `MepoverSharedProject` — never in the version wrapper projects.

## Result reporting back to UI

The handler runs on the Revit thread; the ViewModel lives on the UI thread. Use `Application.Current.Dispatcher.Invoke` to marshal results back:

```csharp
// In MyRequestHandler.Execute():
var results = RunRevitQuery(app);
Application.Current.Dispatcher.Invoke(() =>
{
    _viewModel.Results = results; // triggers INotifyPropertyChanged on UI thread
});
```

## Checklist

- [ ] Handler implements `IExternalEventHandler`
- [ ] `ExternalEvent.Create(handler)` called once at startup
- [ ] ViewModel is static (survives dialog close)
- [ ] `WindowInteropHelper.Owner` set to Revit main window handle
- [ ] Dialog shown with `Show()` not `ShowDialog()`
- [ ] All Revit API calls inside a `Transaction`
- [ ] `Request` reset to `None` at end of `Execute()`
- [ ] Logic in `MepoverSharedProject` only
- [ ] Build: `dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj`
