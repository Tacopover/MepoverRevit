using ClashDetector.ViewModels;
using System.Collections.ObjectModel;

namespace ClashDetector
{
    public class ClashSettings
    {
        public ObservableCollection<ListItem> RevitModels1 { get; set; } = new ObservableCollection<ListItem>();
        public ObservableCollection<ListItem> RevitModels2 { get; set; } = new ObservableCollection<ListItem>();
    }
}
