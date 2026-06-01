using MepoverSharedProject;

namespace SelectionFilter.ViewModels
{
    /// <summary>One preview row in the Selection Filter dialog: value, family/type and data type.</summary>
    public class FilterRow : BaseViewModel
    {
        public string Value { get; }
        public string FamilyAndType { get; }
        public string DataType { get; }

        public FilterRow(string value, string familyAndType, string dataType)
        {
            Value = value;
            FamilyAndType = familyAndType;
            DataType = dataType;
        }
    }
}
