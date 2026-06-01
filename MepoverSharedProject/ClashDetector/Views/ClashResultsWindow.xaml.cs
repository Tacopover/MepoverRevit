using System.Windows;
using System.Windows.Input;
using ClashDetector.ViewModels;

namespace ClashDetector.Views
{
    public sealed partial class ClashResultsWindow : Window
    {
        public ClashResultsWindow()
        {
            InitializeComponent();
        }

        private void ResultsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var vm = DataContext as ClashResultsViewModel;
            vm?.SetSelection(ResultsGrid.SelectedItems);
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
                MaxHeight = SystemParameters.WorkArea.Height;
                WindowState = WindowState.Maximized;
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
