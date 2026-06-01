using ClashDetector;
using MepoverSharedProject;
using System;
using System.Linq;
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

        public RelayCommand<object> RunCommand { get; set; }

        // Raised when a clash run finishes, so the view can open the results window.
        public event Action<ClashResultsViewModel> ResultsReady;

        public ClashDetectorViewModel(IClashService clashService)
        {
            _clashService = clashService;
            SelectedViewModel = ModelsViewModel;

            RunCommand = new RelayCommand<object>(p => true, async p => await RunClashesAsync());
        }

        public async Task RunClashesAsync()
        {
            if (!_clashService.Settings.RevitModels1.Any(m => m.IsSelected) ||
                !_clashService.Settings.RevitModels2.Any(m => m.IsSelected))
            {
                StatusMessage = "Select at least one model on each side (A and B) before running.";
                return;
            }

            StatusMessage = "Running clash detection...";
            try
            {
                var clashes = await _clashService.RunClashesAsync();
                StatusMessage = $"{clashes.Count} clashes found";
                ResultsReady?.Invoke(new ClashResultsViewModel(clashes, _clashService));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }
    }
}
