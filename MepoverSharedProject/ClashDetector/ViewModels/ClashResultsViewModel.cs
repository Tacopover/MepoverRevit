using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using MepoverSharedProject;

namespace ClashDetector.ViewModels
{
    public class ClashResultsViewModel : BaseViewModel
    {
        private readonly IClashService _clashService;
        private List<ClashResultItem> _selected = new List<ClashResultItem>();

        public ObservableCollection<ClashResultItem> Clashes { get; }
        public ICollectionView ClashesView { get; }

        public RelayCommand<object> SelectInRevitCommand { get; }
        public RelayCommand<object> ZoomToClashCommand { get; }

        public ClashResultsViewModel(IReadOnlyList<ClashDto> clashes, IClashService clashService)
        {
            _clashService = clashService;
            Clashes = new ObservableCollection<ClashResultItem>(
                (clashes ?? new List<ClashDto>()).Select(ClashResultItem.FromDto));
            ClashesView = CollectionViewSource.GetDefaultView(Clashes);
            ClashesView.Filter = FilterPredicate;

            SelectInRevitCommand = new RelayCommand<object>(
                p => SelectableElementIds().Count > 0,
                p => _clashService?.SelectInOpenModel(SelectableElementIds()));
            ZoomToClashCommand = new RelayCommand<object>(
                p => SelectableElementIds().Count > 0,
                p => _clashService?.ZoomTo(SelectableElementIds()));
        }

        public int ClashCount
        {
            get { return Clashes.Count; }
        }

        public string SelectionSummary
        {
            get
            {
                int sel = _selected.Count;
                int selectable = SelectableElementIds().Count;
                return $"{sel} clash{(sel == 1 ? "" : "es")} selected · {selectable} host-model element{(selectable == 1 ? "" : "s")} selectable";
            }
        }

        // ── per-column filters ──
        private string _filter1Name, _filter1Category, _filter1Level, _filter1Id;
        private string _filter2Name, _filter2Category, _filter2Level, _filter2Id;

        public string Filter1Name { get => _filter1Name; set { _filter1Name = value; OnFilterChanged(nameof(Filter1Name)); } }
        public string Filter1Category { get => _filter1Category; set { _filter1Category = value; OnFilterChanged(nameof(Filter1Category)); } }
        public string Filter1Level { get => _filter1Level; set { _filter1Level = value; OnFilterChanged(nameof(Filter1Level)); } }
        public string Filter1Id { get => _filter1Id; set { _filter1Id = value; OnFilterChanged(nameof(Filter1Id)); } }
        public string Filter2Name { get => _filter2Name; set { _filter2Name = value; OnFilterChanged(nameof(Filter2Name)); } }
        public string Filter2Category { get => _filter2Category; set { _filter2Category = value; OnFilterChanged(nameof(Filter2Category)); } }
        public string Filter2Level { get => _filter2Level; set { _filter2Level = value; OnFilterChanged(nameof(Filter2Level)); } }
        public string Filter2Id { get => _filter2Id; set { _filter2Id = value; OnFilterChanged(nameof(Filter2Id)); } }

        private void OnFilterChanged(string property)
        {
            OnPropertyChanged(property);
            ClashesView.Refresh();
        }

        private bool FilterPredicate(object o)
        {
            ClashResultItem item = o as ClashResultItem;
            if (item == null)
            {
                return false;
            }
            return Matches(_filter1Name, item.Element1Name)
                && Matches(_filter1Category, item.Element1Category)
                && Matches(_filter1Level, item.Element1Level)
                && Matches(_filter1Id, item.ElementId1.ToString())
                && Matches(_filter2Name, item.Element2Name)
                && Matches(_filter2Category, item.Element2Category)
                && Matches(_filter2Level, item.Element2Level)
                && Matches(_filter2Id, item.ElementId2.ToString());
        }

        private static bool Matches(string filter, string value)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }
            return (value ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── selection (driven from the DataGrid's SelectedItems) ──
        public void SetSelection(IEnumerable selectedItems)
        {
            _selected = selectedItems == null
                ? new List<ClashResultItem>()
                : selectedItems.OfType<ClashResultItem>().ToList();

            SelectInRevitCommand.RaiseCanExecuteChanged();
            ZoomToClashCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        public List<long> SelectableElementIds()
        {
            return _selected.SelectMany(c => c.OpenModelElementIds).Distinct().ToList();
        }
    }
}
