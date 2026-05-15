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
        private IfcExportDialog _window;
        private IdleExportOrchestrator _orchestrator;

        public bool IsWindowClosed { get; private set; } = true;

        public IfcExportDialog Window => _window;

        #region Settings

        private string _destinationFolder = string.Empty;
        public string DestinationFolder
        {
            get { return _destinationFolder; }
            set { _destinationFolder = value; OnPropertyChanged(nameof(DestinationFolder)); }
        }

        private string _selectedIfcVersion = "IFC2x3";
        public string SelectedIfcVersion
        {
            get { return _selectedIfcVersion; }
            set { _selectedIfcVersion = value; OnPropertyChanged(nameof(SelectedIfcVersion)); }
        }

        public string[] IfcVersions { get; } = { "IFC2x3", "IFC4", "IFC4.3" };

        private string _psetMappingFilePath = string.Empty;
        public string PsetMappingFilePath
        {
            get { return _psetMappingFilePath; }
            set { _psetMappingFilePath = value; OnPropertyChanged(nameof(PsetMappingFilePath)); }
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

        #endregion

        public IfcExportViewModel(UIApplication uiApp, IfcExportRequestHandler handler, ExternalEvent externalEvent)
        {
            _uiApp = uiApp;
            _handler = handler;
            _externalEvent = externalEvent;
            RunCommand = new RelayCommand<object>(p => CanRun(), p => OnRun());
            PauseCommand = new RelayCommand<object>(p => CanPause(), p => OnPause());
            CancelCommand = new RelayCommand<object>(p => CanCancel(), p => OnCancel());
            BrowseCommand = new RelayCommand<object>(p => true, p => OnBrowse());
            BrowsePsetCommand = new RelayCommand<object>(p => true, p => OnBrowsePset());
            QuickExportCommand = new RelayCommand<object>(p => true, p => OnQuickExport());
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
            RefreshCommands();
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
        }

        /// <summary>Called by IdleExportOrchestrator when state changes.</summary>
        public void OnOrchestratorStateChanged()
        {
            ExportState state = _orchestrator != null ? _orchestrator.State : ExportState.Idle;
            switch (state)
            {
                case ExportState.Idle: StatusText = "Ready"; break;
                case ExportState.Running: StatusText = "Running\u2026"; break;
                case ExportState.Paused: StatusText = "Paused"; break;
                case ExportState.Completed: StatusText = "Completed"; break;
                case ExportState.Cancelled: StatusText = "Cancelled"; break;
                default: StatusText = "Ready"; break;
            }
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
