using ClashDetector;
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
