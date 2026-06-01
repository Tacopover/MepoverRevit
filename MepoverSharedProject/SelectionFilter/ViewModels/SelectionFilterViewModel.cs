using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject;
using MepoverSharedProject.Common;

namespace SelectionFilter.ViewModels
{
    /// <summary>
    /// Narrows the current selection by a parameter/operator/value test, then writes the filtered
    /// set back as the Revit selection. No model transaction is needed (selection only).
    /// </summary>
    public class SelectionFilterViewModel : ToolViewModel
    {
        private const string TagTextPseudoParameter = "TagText";

        private readonly UIDocument _uidoc;
        private List<Element> _working;

        public ObservableCollection<string> ParameterNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Operators { get; } = new ObservableCollection<string>
        {
            "equals", "does not equal", "is greater than", "is greater than or equal",
            "is less than", "is less than or equal", "contains", "does not contain"
        };
        public ObservableCollection<FilterRow> Rows { get; } = new ObservableCollection<FilterRow>();

        private string _selectedParameter;
        public string SelectedParameter
        {
            get => _selectedParameter;
            set { _selectedParameter = value; OnPropertyChanged(nameof(SelectedParameter)); UpdatePreview(); }
        }

        private string _selectedOperator;
        public string SelectedOperator
        {
            get => _selectedOperator;
            set { _selectedOperator = value; OnPropertyChanged(nameof(SelectedOperator)); }
        }

        private string _valueText = string.Empty;
        public string ValueText
        {
            get => _valueText;
            set { _valueText = value; OnPropertyChanged(nameof(ValueText)); }
        }

        private bool _caseSensitive;
        public bool CaseSensitive
        {
            get => _caseSensitive;
            set { _caseSensitive = value; OnPropertyChanged(nameof(CaseSensitive)); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public RelayCommand<object> ApplyCommand { get; }
        public RelayCommand<object> CloseCommand { get; }

        public SelectionFilterViewModel(UIDocument uidoc, ICollection<ElementId> selectedIds)
        {
            _uidoc = uidoc;
            Document doc = uidoc.Document;
            _working = (selectedIds ?? new List<ElementId>())
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            SelectedOperator = Operators.First();
            PopulateParameterNames();

            ApplyCommand = new RelayCommand<object>(p => true, p => Apply());
            CloseCommand = new RelayCommand<object>(p => true, p => Close());

            StatusMessage = $"{_working.Count} element(s) selected.";
        }

        private void PopulateParameterNames()
        {
            SortedSet<string> names = new SortedSet<string>();
            bool hasTags = false;
            foreach (Element element in _working)
            {
                foreach (Parameter p in element.Parameters)
                {
                    names.Add(p.Definition.Name);
                }
                if (element is IndependentTag)
                {
                    hasTags = true;
                }
            }
            if (hasTags)
            {
                names.Add(TagTextPseudoParameter);
            }

            ParameterNames.Clear();
            foreach (string name in names)
            {
                ParameterNames.Add(name);
            }
            SelectedParameter = ParameterNames.FirstOrDefault();
        }

        private void Apply()
        {
            if (string.IsNullOrEmpty(SelectedParameter) || string.IsNullOrEmpty(SelectedOperator))
            {
                return;
            }

            List<Element> filtered = new List<Element>();
            foreach (Element element in _working)
            {
                string value = GetValueString(element, SelectedParameter);
                if (value == null)
                {
                    continue; // element does not carry this parameter
                }
                if (Matches(value, SelectedOperator, ValueText))
                {
                    filtered.Add(element);
                }
            }

            _working = filtered;
            StatusMessage = $"{_working.Count} element(s) match.";
            UpdatePreview();
        }

        private void Close()
        {
            _uidoc.Selection.SetElementIds(_working.Select(e => e.Id).ToList());
            CloseDialog(true);
        }

        private void UpdatePreview()
        {
            Rows.Clear();
            if (string.IsNullOrEmpty(SelectedParameter))
            {
                return;
            }
            int count = 0;
            foreach (Element element in _working)
            {
                string value = GetValueString(element, SelectedParameter);
                if (value == null)
                {
                    continue;
                }
                Rows.Add(new FilterRow(value, GetFamilyAndType(element), GetDataType(element, SelectedParameter)));
                count++;
            }
            StatusMessage = $"{count} element(s) have '{SelectedParameter}'.";
        }

        private bool Matches(string parameterValue, string op, string userInput)
        {
            switch (op)
            {
                case "equals":
                    return StringEquals(parameterValue, userInput);
                case "does not equal":
                    return !StringEquals(parameterValue, userInput);
                case "contains":
                    return Normalize(parameterValue).Contains(Normalize(userInput));
                case "does not contain":
                    return !Normalize(parameterValue).Contains(Normalize(userInput));
                case "is greater than":
                    return TryNumbers(parameterValue, userInput, out double a1, out double b1) && a1 > b1;
                case "is greater than or equal":
                    return TryNumbers(parameterValue, userInput, out double a2, out double b2) && a2 >= b2;
                case "is less than":
                    return TryNumbers(parameterValue, userInput, out double a3, out double b3) && a3 < b3;
                case "is less than or equal":
                    return TryNumbers(parameterValue, userInput, out double a4, out double b4) && a4 <= b4;
                default:
                    return false;
            }
        }

        private bool StringEquals(string a, string b)
        {
            return Normalize(a) == Normalize(b);
        }

        private string Normalize(string value)
        {
            value = value ?? string.Empty;
            return CaseSensitive ? value : value.ToLowerInvariant();
        }

        // Parses the leading numeric token of each side (handles "150 mm", "Yes"/"No") culture-invariantly.
        private static bool TryNumbers(string left, string right, out double leftValue, out double rightValue)
        {
            return TryParseNumber(left, out leftValue) & TryParseNumber(right, out rightValue);
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            text = text.Trim();
            if (text.Equals("Yes", System.StringComparison.OrdinalIgnoreCase)) { value = 1; return true; }
            if (text.Equals("No", System.StringComparison.OrdinalIgnoreCase)) { value = 0; return true; }

            string token = text.Split(' ')[0].Replace(",", ".");
            return double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        private static string GetValueString(Element element, string parameterName)
        {
            if (parameterName == TagTextPseudoParameter)
            {
                return element is IndependentTag tag ? tag.TagText : null;
            }

            IList<Parameter> found = element.GetParameters(parameterName);
            if (found.Count == 0)
            {
                return null;
            }
            Parameter p = PreferBuiltIn(found);
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? string.Empty;
                case StorageType.Integer:
                case StorageType.Double:
                case StorageType.ElementId:
                    return p.AsValueString() ?? string.Empty;
                default:
                    return null;
            }
        }

        private static string GetDataType(Element element, string parameterName)
        {
            if (parameterName == TagTextPseudoParameter)
            {
                return "String";
            }
            IList<Parameter> found = element.GetParameters(parameterName);
            return found.Count == 0 ? string.Empty : PreferBuiltIn(found).StorageType.ToString();
        }

        private static Parameter PreferBuiltIn(IList<Parameter> parameters)
        {
            foreach (Parameter p in parameters)
            {
                if (p.Definition is InternalDefinition def && def.BuiltInParameter != BuiltInParameter.INVALID)
                {
                    return p;
                }
            }
            return parameters[0];
        }

        private static string GetFamilyAndType(Element element)
        {
            IList<Parameter> familyAndType = element.GetParameters("Family and Type");
            if (familyAndType.Count > 0)
            {
                string value = familyAndType[0].AsValueString();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return element.Category?.Name ?? element.Name;
        }
    }
}
