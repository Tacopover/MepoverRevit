using System.Windows;
using ClashDetector.ViewModels;
using ClashDetector.Views;

namespace ClashDetector.DevHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var viewModel = new ClashDetectorViewModel(new MockClashService());
            var window = new ClashDetectorWindow { DataContext = viewModel };
            window.Show();
        }
    }
}
