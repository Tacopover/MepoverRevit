using Autodesk.Revit.DB;
using MepoverSharedProject;

namespace AlignViewports
{
    /// <summary>One selectable sheet row in the Align Viewports dialog.</summary>
    public class SheetListItem : BaseViewModel
    {
        public ViewSheet Sheet { get; }
        public string Title { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public SheetListItem(ViewSheet sheet)
        {
            Sheet = sheet;
            Title = sheet.SheetNumber + " - " + sheet.Name;
        }
    }
}
