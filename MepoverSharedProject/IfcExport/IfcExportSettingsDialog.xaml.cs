using System.Windows;
using System.Windows.Input;

namespace IfcExport
{
    public partial class IfcExportSettingsDialog : Window
    {
        public IfcExportSettingsDialog()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
