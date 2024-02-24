using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MepoverSharedProject.SheetCopier
{
    public partial class SheetCopierWindow : Window
    {
        public SheetCopierWindow()
        {
            InitializeComponent();
        }

        private void FilterDoubleClick(object sender, EventArgs e)
        {
            System.Windows.Controls.TextBox textbox = sender as System.Windows.Controls.TextBox;
            //do nothing, just testing
        }

        private void HeaderTextChanged(object sender, EventArgs e)
        {
            System.Windows.Controls.TextBox textbox = sender as System.Windows.Controls.TextBox;
            string filterText = textbox.Text;

            DataGridColumnHeader columnHeader = FindParent<DataGridColumnHeader>(textbox);

            if (columnHeader != null)
            {
                FilterHeader(filterText, columnHeader);
            }
        }

        public void FilterHeader(string filterText, DataGridColumnHeader columnHeader)
        {
            var viewModel = DataContext as SheetCopierViewModel;
            viewModel.FilterHeader(filterText, columnHeader);
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            if (parent == null)
                return null;

            if (parent is T typedParent)
                return typedParent;

            return FindParent<T>(parent);
        }
        public void Dispose()
        {
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
