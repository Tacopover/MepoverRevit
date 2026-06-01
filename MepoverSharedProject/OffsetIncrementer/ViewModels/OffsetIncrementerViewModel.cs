using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject;
using MepoverSharedProject.Common;

namespace OffsetIncrementer.ViewModels
{
    /// <summary>
    /// Adds a fixed increment (mm) to the offset parameter of the selected MEP elements.
    /// Works on the internal value directly so it is independent of the project's display units.
    /// </summary>
    public class OffsetIncrementerViewModel : ToolViewModel
    {
        private const double MmPerFoot = 304.8;
        private static readonly string[] OffsetParameterNames = { "Elevation from Level", "Middle Elevation" };

        private readonly Document _doc;
        private readonly List<Element> _elements;

        public ObservableCollection<OffsetRow> Rows { get; } = new ObservableCollection<OffsetRow>();

        private string _incrementText = "0";
        public string IncrementText
        {
            get => _incrementText;
            set { _incrementText = value; OnPropertyChanged(nameof(IncrementText)); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public RelayCommand<object> ApplyCommand { get; }
        public RelayCommand<object> CloseCommand { get; }

        public OffsetIncrementerViewModel(UIDocument uidoc, ICollection<ElementId> selectedIds)
        {
            _doc = uidoc.Document;
            _elements = (selectedIds ?? new List<ElementId>())
                .Select(id => _doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            ApplyCommand = new RelayCommand<object>(p => true, p => Apply());
            CloseCommand = new RelayCommand<object>(p => true, p => CloseDialog(true));

            RefreshRows();
            if (_elements.Count == 0)
            {
                StatusMessage = "Nothing selected. Select elements before running.";
            }
        }

        private void RefreshRows()
        {
            Rows.Clear();
            foreach (Element element in _elements)
            {
                Parameter offsetParam = GetOffsetParameter(element);
                if (offsetParam == null)
                {
                    continue;
                }
                Rows.Add(new OffsetRow(element, GetFamilyAndType(element), offsetParam.AsValueString()));
            }
        }

        private void Apply()
        {
            if (!double.TryParse(IncrementText, NumberStyles.Any, CultureInfo.CurrentCulture, out double incrementMm) &&
                !double.TryParse(IncrementText, NumberStyles.Any, CultureInfo.InvariantCulture, out incrementMm))
            {
                StatusMessage = "Increment must be a number (mm).";
                return;
            }

            double incrementFeet = incrementMm / MmPerFoot;
            int changed = 0;
            using (Transaction trans = new Transaction(_doc, "MEPover: Increment Offset"))
            {
                trans.Start();
                foreach (Element element in _elements)
                {
                    Parameter offsetParam = GetOffsetParameter(element);
                    if (offsetParam == null || offsetParam.IsReadOnly)
                    {
                        continue;
                    }
                    offsetParam.Set(offsetParam.AsDouble() + incrementFeet);
                    changed++;
                }
                trans.Commit();
            }

            RefreshRows();
            StatusMessage = $"Offset incremented on {changed} element(s).";
        }

        private static Parameter GetOffsetParameter(Element element)
        {
            foreach (string name in OffsetParameterNames)
            {
                IList<Parameter> found = element.GetParameters(name);
                if (found.Count > 0)
                {
                    return PreferBuiltIn(found);
                }
            }
            return null;
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
