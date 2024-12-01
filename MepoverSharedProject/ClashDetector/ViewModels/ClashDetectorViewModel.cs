using Autodesk.Revit.UI;
using ClashDetector.Views;
using ClashDetectorUI;
using MepoverSharedProject;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using UIFramework;

namespace ClashDetector.ViewModels
{
    public class ClashDetectorViewModel : BaseViewModel
    {
        private RevitClashService revitService;

        #region properties

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


        private HostLinkViewModel _modelsViewModel;
        public HostLinkViewModel ModelsViewModel
        {
            get
            {
                if (_modelsViewModel == null)
                {
                    _modelsViewModel = new HostLinkViewModel(revitService.Settings);
                }
                return _modelsViewModel;
            }
            set
            {
                _modelsViewModel = value;
            }
        }


        private CategoriesViewModel _categoryViewModel;
        public CategoriesViewModel CategoryViewModel
        {
            get
            {
                if (_categoryViewModel == null)
                {
                    _categoryViewModel = new CategoriesViewModel(revitService.Settings);
                }
                return _categoryViewModel;
            }
            set
            {
                _categoryViewModel = value;
            }
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
            set
            {
                _worksetViewModel = value;
            }
        }

        public bool IsWindowClosed { get; set; } = true;

        private ClashDetectorWindow _mainWindow;
        public ClashDetectorWindow MainWindow
        {
            get
            {
                if (_mainWindow == null)
                {
                    _mainWindow = new ClashDetectorWindow() { DataContext = this };
                }
                return _mainWindow;
            }
            set
            {
                _mainWindow = value;
                OnPropertyChanged(nameof(MainWindow));
            }
        }


        #endregion

        public RelayCommand<object> NavigateToCommand { get; set; }
        public RelayCommand<object> RunCommand { get; set; }


        public ClashDetectorViewModel(RevitClashService revitService)
        {
            this.revitService = revitService;
            SelectedViewModel = new CategoriesViewModel(revitService.Settings);

            NavigateToCommand = new RelayCommand<object>(p => true, p => NavigateTo(p));
            RunCommand = new RelayCommand<object>(p => true, p => RunClashes());


            ShowMainWindow();

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
            revitService.SendMessage("Switched to " + SelectedButton);
        }

        private void RunClashes()
        {
            revitService.RunClashes();
        }

        public void ShowMainWindow()
        {
            if (IsWindowClosed)
            {
                MainWindow = new ClashDetectorWindow() { DataContext = this };
                //handler = new RequestHandler(this, revitService);
                //exEvent = ExternalEvent.Create(handler);
                WindowInteropHelper helper = new WindowInteropHelper(MainWindow);
                helper.Owner = revitService.UIApp.MainWindowHandle;
                MainWindow.ShowDialog();
                IsWindowClosed = false;
                MainWindow.Closed += MainWindow_Closed;
            }
            else
            {
                MainWindow.Activate();
            }
        }
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            //exEvent.Dispose();
            //exEvent = null;
            //handler = null;
            IsWindowClosed = true;
            MainWindow.Closed -= MainWindow_Closed;
        }
    }
}
