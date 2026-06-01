using Autodesk.Revit.DB;
using MepoverSharedProject;

namespace OffsetIncrementer.ViewModels
{
    /// <summary>A selected element's family/type and current offset, shown in the preview list.</summary>
    public class OffsetRow : BaseViewModel
    {
        public Element Element { get; }
        public string FamilyAndType { get; }

        private string _offset;
        public string Offset
        {
            get => _offset;
            set { _offset = value; OnPropertyChanged(nameof(Offset)); }
        }

        public OffsetRow(Element element, string familyAndType, string offset)
        {
            Element = element;
            FamilyAndType = familyAndType;
            Offset = offset;
        }
    }
}
