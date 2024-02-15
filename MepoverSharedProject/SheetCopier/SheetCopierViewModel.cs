using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject.Utilities;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Interop;
using UIFramework;

namespace MepoverSharedProject.SheetCopier
{
    public class SheetCopierViewModel : BaseViewModel
    {
        #region images
        private System.Windows.Media.ImageSource _minimizeImage;
        public System.Windows.Media.ImageSource MinimizeImage
        {
            get { return _minimizeImage; }
            set
            {
                _minimizeImage = value;
                OnPropertyChanged(nameof(MinimizeImage));
            }
        }
        private System.Windows.Media.ImageSource _maximizeImage;
        public System.Windows.Media.ImageSource MaximizeImage
        {
            get { return _maximizeImage; }
            set
            {
                _maximizeImage = value;
                OnPropertyChanged(nameof(MaximizeImage));
            }
        }
        private System.Windows.Media.ImageSource _closeImage;
        public System.Windows.Media.ImageSource CloseImage
        {
            get { return _closeImage; }
            set
            {
                _closeImage = value;
                OnPropertyChanged(nameof(CloseImage));
            }
        }

        #endregion
        private SheetCopierWindow _mainWindow;
        public SheetCopierWindow MainWindow
        {
            get
            {
                if (_mainWindow == null)
                {
                    _mainWindow = new SheetCopierWindow() { DataContext = this };
                }
                return _mainWindow;
            }
            set
            {
                _mainWindow = value;
                OnPropertyChanged(nameof(MainWindow));
            }
        }

        public SheetCopierViewModel()
        {
            MinimizeImage = ConvertToEmbeddedImage("minimizeButton.png");
            MaximizeImage = ConvertToEmbeddedImage("maximizeButton.png");
            CloseImage = ConvertToEmbeddedImage("closeButton.png");
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            bool isClosed = true;
            //var allprocesses = Process.GetProcesses();
            //foreach (var p in allprocesses)
            //{
            //    if (p.MainWindowHandle == IntPtr.Zero)
            //    {
            //        continue;
            //    }
            //    if (p.MainWindowHandle == RevitCommand.WindowHandle)
            //    {
            //        isClosed = false;
            //        break;
            //    }
            //}
            MainWindow.Show();
            //if (isClosed)
            //{
            //    WindowInteropHelper helper = new WindowInteropHelper(MainWindow);
            //    helper.Owner = uiApp.MainWindowHandle;
            //    MainWindow.Show();
            //    MainWindow.Closed += MainWindow_Closed;
            //}
            //else
            //{
            //    MainWindow.Activate();
            //}
        }

        private System.Windows.Media.ImageSource ConvertToEmbeddedImage(string imagePath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var img = new System.Windows.Media.Imaging.BitmapImage();
            return Utils.LoadEmbeddedImage(assembly, imagePath);
        }
    }
}
