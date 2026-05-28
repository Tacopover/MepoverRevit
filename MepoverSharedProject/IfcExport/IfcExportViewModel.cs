using Autodesk.Revit.UI;
using MepoverSharedProject;
using System.IO;
using System.Windows.Interop;

namespace IfcExport
{
    /// <summary>
    /// ViewModel for the IFC Export dialog.
    /// Follows BaseViewModel / RelayCommand conventions used across this codebase.
    /// </summary>
    public class IfcExportViewModel : BaseViewModel
    {
        private readonly UIApplication _uiApp;
        private readonly IfcExportRequestHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly IfcExportPersistenceService _persistence;
        private IfcExportDialog _window;
        private IdleExportOrchestrator _orchestrator;

        private string ActiveDocTitle => _uiApp.ActiveUIDocument?.Document?.Title ?? string.Empty;

        public bool IsWindowClosed { get; private set; } = true;

        public IfcExportDialog Window => _window;

        #region Settings

        private string _destinationFolder = string.Empty;
        public string DestinationFolder
        {
            get { return _destinationFolder; }
            set
            {
                _destinationFolder = value;
                OnPropertyChanged(nameof(DestinationFolder));
                if (_orchestrator?.State == ExportState.Running)
                    _persistence?.SaveDocument(ActiveDocTitle, DestinationFolder, SelectedIfcVersion, PsetMappingFilePath);
            }
        }

        private string _selectedIfcVersion = "IFC2x3";
        public string SelectedIfcVersion
        {
            get { return _selectedIfcVersion; }
            set
            {
                _selectedIfcVersion = value;
                OnPropertyChanged(nameof(SelectedIfcVersion));
                if (_orchestrator?.State == ExportState.Running)
                    _persistence?.SaveDocument(ActiveDocTitle, DestinationFolder, SelectedIfcVersion, PsetMappingFilePath);
            }
        }

        public string[] IfcVersions { get; } = { "IFC2x3", "IFC4", "IFC4.3" };

        private string _psetMappingFilePath = string.Empty;
        public string PsetMappingFilePath
        {
            get { return _psetMappingFilePath; }
            set
            {
                _psetMappingFilePath = value;
                OnPropertyChanged(nameof(PsetMappingFilePath));
                if (_orchestrator?.State == ExportState.Running)
                    _persistence?.SaveDocument(ActiveDocTitle, DestinationFolder, SelectedIfcVersion, PsetMappingFilePath);
            }
        }

        #endregion

        #region Progress

        private string _statusText = "Ready";
        public string StatusText
        {
            get { return _statusText; }
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get { return _progressValue; }
            set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
        }

        private double _progressMax = 100;
        public double ProgressMax
        {
            get { return _progressMax; }
            set { _progressMax = value; OnPropertyChanged(nameof(ProgressMax)); }
        }

        private int _pendingChanges;
        public int PendingChanges
        {
            get { return _pendingChanges; }
            set { _pendingChanges = value; OnPropertyChanged(nameof(PendingChanges)); }
        }

        #endregion

        #region Commands

        public RelayCommand<object> RunCommand { get; }
        public RelayCommand<object> PauseCommand { get; }
        public RelayCommand<object> CancelCommand { get; }
        public RelayCommand<object> BrowseCommand { get; }
        public RelayCommand<object> BrowsePsetCommand { get; }
        public RelayCommand<object> QuickExportCommand { get; }
        public RelayCommand<object> ExportViewCommand { get; }
        public RelayCommand<object> ToggleExportCommand { get; }
        public RelayCommand<object> OpenSettingsCommand { get; }

        public string ToggleExportLabel
        {
            get
            {
                if (_orchestrator == null
                    || _orchestrator.State == ExportState.Idle
                    || _orchestrator.State == ExportState.Cancelled
                    || _orchestrator.State == ExportState.Completed)
                    return "▶  Export is off";
                if (_orchestrator.State == ExportState.Running)
                    return "⏸  Export is on";
                return "▶  Export is paused";
            }
        }

        public bool IsExportActive =>
            _orchestrator?.State == ExportState.Running
            || _orchestrator?.State == ExportState.Paused;

        public string OrchestratorStateName
        {
            get
            {
                if (_orchestrator == null) return "IDLE";
                if (_orchestrator.State == ExportState.Running) return "RUNNING";
                if (_orchestrator.State == ExportState.Paused)  return "PAUSED";
                return "IDLE";
            }
        }

        #endregion

        public IfcExportViewModel(UIApplication uiApp, IfcExportRequestHandler handler, ExternalEvent externalEvent, IfcExportPersistenceService persistence = null)
        {
            _uiApp = uiApp;
            _handler = handler;
            _externalEvent = externalEvent;
            _persistence = persistence;
            RunCommand = new RelayCommand<object>(p => CanRun(), p => OnRun());
            PauseCommand = new RelayCommand<object>(p => CanPause(), p => OnPause());
            CancelCommand = new RelayCommand<object>(p => CanCancel(), p => OnCancel());
            BrowseCommand = new RelayCommand<object>(p => true, p => OnBrowse());
            BrowsePsetCommand = new RelayCommand<object>(p => true, p => OnBrowsePset());
            QuickExportCommand = new RelayCommand<object>(p => true, p => OnQuickExport());
            ExportViewCommand = new RelayCommand<object>(p => CanExportView(), p => OnExportView());
            ToggleExportCommand = new RelayCommand<object>(p => true, p => OnToggleExport());
            OpenSettingsCommand = new RelayCommand<object>(p => true, p => OnOpenSettings());
        }

        private bool CanRun()
        {
            return _orchestrator == null
                || _orchestrator.State == ExportState.Idle
                || _orchestrator.State == ExportState.Cancelled
                || _orchestrator.State == ExportState.Completed;
        }

        private bool CanPause()
        {
            return _orchestrator != null
                && (_orchestrator.State == ExportState.Running
                 || _orchestrator.State == ExportState.Paused);
        }

        private bool CanCancel()
        {
            return _orchestrator != null
                && _orchestrator.State != ExportState.Idle
                && _orchestrator.State != ExportState.Cancelled
                && _orchestrator.State != ExportState.Completed;
        }

        /// <summary>Restores settings from persistence without triggering a save.</summary>
        internal void ApplySettings(IfcExportDocumentSettings settings)
        {
            _destinationFolder = settings.DestinationFolder;
            _selectedIfcVersion = settings.IfcVersion;
            _psetMappingFilePath = settings.PsetMappingFilePath;
            OnPropertyChanged(nameof(DestinationFolder));
            OnPropertyChanged(nameof(SelectedIfcVersion));
            OnPropertyChanged(nameof(PsetMappingFilePath));
        }

        /// <summary>Starts the export without showing the window — used for silent auto-start.</summary>
        internal void TriggerStart() => OnRun();

        private void OnRun()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                StatusText = "Please select a destination folder.";
                return;
            }

            if (_orchestrator == null
                || _orchestrator.State == ExportState.Cancelled
                || _orchestrator.State == ExportState.Completed)
            {
                _orchestrator = new IdleExportOrchestrator(_uiApp, this);
                _handler.Orchestrator = _orchestrator;
            }

            _handler.Request(IfcExportRequest.Start);
            _externalEvent.Raise();
            _persistence?.SaveDocument(ActiveDocTitle, DestinationFolder, SelectedIfcVersion, PsetMappingFilePath);
            RefreshCommands();
        }

        private void OnToggleExport()
        {
            if (_orchestrator == null
                || _orchestrator.State == ExportState.Idle
                || _orchestrator.State == ExportState.Cancelled
                || _orchestrator.State == ExportState.Completed)
                OnRun();
            else
                OnPause();
        }

        private void OnOpenSettings()
        {
            var dlg = new IfcExportSettingsDialog { DataContext = this };
            var helper = new System.Windows.Interop.WindowInteropHelper(dlg);
            helper.Owner = _uiApp.MainWindowHandle;
            dlg.ShowDialog();
        }

        private void OnPause()
        {
            if (_orchestrator == null)
                return;

            IfcExportRequest req = _orchestrator.State == ExportState.Running
                ? IfcExportRequest.Pause
                : IfcExportRequest.Resume;

            _handler.Request(req);
            _externalEvent.Raise();
            RefreshCommands();
        }

        private void OnCancel()
        {
            if (_orchestrator != null)
            {
                _handler.Request(IfcExportRequest.Cancel);
                _externalEvent.Raise();
            }
            _persistence?.RemoveDocument(ActiveDocTitle);
            RefreshCommands();
        }

        private bool CanQuickExport() =>
            !string.IsNullOrWhiteSpace(DestinationFolder);

        private void OnQuickExport()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                StatusText = "Please select a destination folder.";
                return;
            }

            StatusText = "Quick export running\u2026";
            _handler.QuickExportDestination = DestinationFolder;
            _handler.QuickExportCallback = result =>
            {
                StatusText = result.StartsWith("Error:") ? result : "Saved: " + result;
                QuickExportCommand.RaiseCanExecuteChanged();
            };
            _handler.Request(IfcExportRequest.QuickExport);
            _externalEvent.Raise();
        }

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

        private void OnBrowsePset()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select custom property set mapping file",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
                PsetMappingFilePath = dlg.FileName;
        }

        /// <summary>
        /// Uses a standard OpenFileDialog as a folder-picker workaround (no extra dependencies).
        /// The user navigates to the target folder, and we take the directory of whatever they "open".
        /// </summary>
        private void OnBrowse()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Navigate to the IFC output folder — open any file within it, or type the path",
                FileName = "Select this folder",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            if (dlg.ShowDialog() == true)
            {
                string folder = Path.GetDirectoryName(dlg.FileName);
                DestinationFolder = string.IsNullOrEmpty(folder) ? dlg.FileName : folder;
            }
        }

        private void RefreshCommands()
        {
            RunCommand.RaiseCanExecuteChanged();
            PauseCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            ExportViewCommand.RaiseCanExecuteChanged();
            ToggleExportCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleExportLabel));
            OnPropertyChanged(nameof(IsExportActive));
            OnPropertyChanged(nameof(OrchestratorStateName));
        }

        /// <summary>Called by IdleExportOrchestrator when state changes.</summary>
        public void OnOrchestratorStateChanged()
        {
            OnPropertyChanged(nameof(OrchestratorStateName));
            OnPropertyChanged(nameof(IsExportActive));
            RefreshCommands();
        }

        public void ShowWindow()
        {
            if (IsWindowClosed)
            {
                _window = new IfcExportDialog { DataContext = this };
                var helper = new WindowInteropHelper(_window);
                helper.Owner = _uiApp.MainWindowHandle;
                _window.Show();
                IsWindowClosed = false;
                _window.Closed += (s, e) => { IsWindowClosed = true; };
            }
            else
            {
                if (_window != null)
                    _window.Activate();
            }
        }
    }
}
