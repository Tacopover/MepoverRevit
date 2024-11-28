using ClashDetectorUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ClashDetectorUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public MainViewModel ViewModel;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Create the startup window

            //MainWindow wnd = new MainWindow();
            //ViewModel = new MainViewModel();
            //wnd.DataContext = viewModel;
            // Do stuff here, e.g. to the window
            //wnd.Title = "Something else";
            // Show the window
            //wnd.Show();

            ////Below does not work because of all the references.... would maybe work with a MVVM setup
            //SettingsMenu menu = new SettingsMenu();
            //menu.Show();
        }
    }
}
