using ClashDetectorUI.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashDetectorUI.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private string test;
        public string Test
        {
            get { return test; }
            set
            {
                test = value;
                OnPropertyChanged(nameof(Test));
            }
        }

        private string result;
        public string Result
        {
            get { return result; }
            set
            {
                if (result != value)
                {
                    result = value;
                    OnPropertyChanged(nameof(Result));
                    ResultChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler ResultChanged;

        RelayCommand<object> RunClashCommand { get; set; }
        public MainViewModel()
        {
            Result = "Hello World!";
            RunClashCommand = new RelayCommand<object>(p => true, p => RunClash());
        }

        private void RunClash()
        {
            Result = "Run Clashes";
        }
    }
}
