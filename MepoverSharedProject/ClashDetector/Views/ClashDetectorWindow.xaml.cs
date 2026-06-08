using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClashDetector.ViewModels;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace ClashDetector.Views
{
    public sealed partial class ClashDetectorWindow : Window
    {
        private ClashResultsWindow _resultsWindow;

        public IntPtr RevitMainWindowHandle { get; set; }

        public ClashDetectorWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ClashDetectorViewModel oldVm)
            {
                oldVm.ResultsReady -= ShowResults;
            }
            if (e.NewValue is ClashDetectorViewModel newVm)
            {
                newVm.ResultsReady += ShowResults;
            }
        }

        private void ShowResults(ClashResultsViewModel resultsViewModel)
        {
            if (_resultsWindow != null && _resultsWindow.IsLoaded)
            {
                _resultsWindow.DataContext = resultsViewModel;
                _resultsWindow.Activate();
            }
            else
            {
                _resultsWindow = new ClashResultsWindow { DataContext = resultsViewModel };
                if (RevitMainWindowHandle != IntPtr.Zero)
                    new WindowInteropHelper(_resultsWindow).Owner = RevitMainWindowHandle;
                _resultsWindow.Closed += (s, e) => _resultsWindow = null;
                _resultsWindow.Show();
            }

            // Close the setup window — results window is now parented to Revit directly
            Close();
        }

        private void ButtonMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ButtonMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                // Get the working area of the screen (excluding the taskbar)
                var workingArea = System.Windows.SystemParameters.WorkArea;

                // Adjust the window size to fit within the working area
                MaxHeight = workingArea.Height;

                WindowState = WindowState.Maximized;
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }


    }
}
