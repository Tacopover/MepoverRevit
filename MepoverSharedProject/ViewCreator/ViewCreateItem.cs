using Autodesk.Revit.DB;
using MepoverSharedProject;

namespace ViewCreator
{
    /// <summary>A checkable row (view template or level) in the View Creator dialog.</summary>
    public class ViewCreateItem : BaseViewModel
    {
        public Element Element { get; }
        public string Name { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }

        public ViewCreateItem(Element element, string name)
        {
            Element = element;
            Name = name;
        }
    }
}
