using System;
using System.Windows;
using System.Windows.Input;

namespace MepoverSharedProject.Common
{
    /// <summary>
    /// Shared base for the modal MEPover tool windows. Provides the custom-chrome handlers
    /// (drag, minimize, close) and bridges <see cref="IClosableViewModel.RequestClose"/> to
    /// the window's DialogResult. XAML windows derive from this and wire the title bar /
    /// chrome buttons to the protected handlers below.
    /// </summary>
    public class ToolWindow : Window
    {
        private IClosableViewModel _closable;

        public ToolWindow()
        {
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_closable != null)
                _closable.RequestClose -= OnRequestClose;

            _closable = e.NewValue as IClosableViewModel;

            if (_closable != null)
                _closable.RequestClose += OnRequestClose;
        }

        private void OnRequestClose(bool? result)
        {
            try
            {
                DialogResult = result;
            }
            catch (InvalidOperationException)
            {
                // Window was shown non-modally; DialogResult is only valid for ShowDialog.
            }
            Close();
        }

        protected void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        protected void ButtonMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        protected void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
