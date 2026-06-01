using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject;
using MepoverSharedProject.Common;

namespace AlignViewports.ViewModels
{
    /// <summary>
    /// Aligns the viewports of one or more "slave" sheets to match the viewport layout of a
    /// chosen "master" sheet. Runs the realignment transaction directly (modal, main thread).
    /// </summary>
    public class AlignViewportsViewModel : ToolViewModel
    {
        private readonly Document _doc;
        private readonly List<SheetListItem> _allSheets;

        public ObservableCollection<SheetListItem> Sheets { get; } = new ObservableCollection<SheetListItem>();
        public ObservableCollection<SheetListItem> SlaveSheets { get; } = new ObservableCollection<SheetListItem>();

        private SheetListItem _selectedMaster;
        public SheetListItem SelectedMaster
        {
            get => _selectedMaster;
            set { _selectedMaster = value; OnPropertyChanged(nameof(SelectedMaster)); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(nameof(SearchText)); ApplyFilter(); }
        }

        private bool _ignoreCase = true;
        public bool IgnoreCase
        {
            get => _ignoreCase;
            set { _ignoreCase = value; OnPropertyChanged(nameof(IgnoreCase)); ApplyFilter(); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public RelayCommand<object> ApplyCommand { get; }
        public RelayCommand<object> CloseCommand { get; }

        public AlignViewportsViewModel(UIDocument uidoc)
        {
            _doc = uidoc.Document;

            _allSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .Select(s => new SheetListItem(s))
                .ToList();

            foreach (SheetListItem item in _allSheets)
            {
                Sheets.Add(item);
                SlaveSheets.Add(item);
            }

            ApplyCommand = new RelayCommand<object>(p => true, p => Apply());
            CloseCommand = new RelayCommand<object>(p => true, p => CloseDialog(false));
        }

        private void ApplyFilter()
        {
            SlaveSheets.Clear();
            IEnumerable<SheetListItem> filtered = _allSheets;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = IgnoreCase ? SearchText.ToLowerInvariant() : SearchText;
                filtered = _allSheets.Where(s =>
                    (IgnoreCase ? s.Title.ToLowerInvariant() : s.Title).Contains(term));
            }
            foreach (SheetListItem item in filtered)
            {
                SlaveSheets.Add(item);
            }
        }

        private void Apply()
        {
            if (SelectedMaster == null)
            {
                StatusMessage = "Select a master sheet first.";
                return;
            }

            List<ViewSheet> slaves = _allSheets
                .Where(s => s.IsSelected && s.Sheet.Id != SelectedMaster.Sheet.Id)
                .Select(s => s.Sheet)
                .ToList();

            if (slaves.Count == 0)
            {
                StatusMessage = "Check at least one slave sheet (other than the master).";
                return;
            }

            using (Transaction trans = new Transaction(_doc, "MEPover: Align Viewports"))
            {
                trans.Start();
                SetViewports(SelectedMaster.Sheet, slaves);
                trans.Commit();
            }

            CloseDialog(true);
        }

        private void SetViewports(ViewSheet master, List<ViewSheet> slaves)
        {
            List<XYZ> masterLocations = new List<XYZ>();
            foreach (ElementId id in master.GetAllViewports())
            {
                Viewport viewport = _doc.GetElement(id) as Viewport;
                XYZ center = viewport.GetBoxCenter();
                masterLocations.Add(new XYZ(center.X, center.Y, 0));
            }

            if (masterLocations.Count == 0)
            {
                StatusMessage = "The master sheet has no viewports to align to.";
                return;
            }

            foreach (ViewSheet slave in slaves)
            {
                List<ElementId> idList = slave.GetAllViewports().ToList();
                if (idList.Count > masterLocations.Count)
                {
                    // More slave viewports than master positions: pull the nearest slave to each master slot.
                    foreach (XYZ masterLoc in masterLocations)
                    {
                        Viewport viewport = ClosestSlave(masterLoc, idList);
                        viewport?.SetBoxCenter(masterLoc);
                    }
                }
                else
                {
                    foreach (ElementId id in idList)
                    {
                        Viewport viewport = _doc.GetElement(id) as Viewport;
                        XYZ newCenter = ClosestLocation(viewport.GetBoxCenter(), masterLocations);
                        viewport.SetBoxCenter(newCenter);
                    }
                }
            }
        }

        private static XYZ ClosestLocation(XYZ location, List<XYZ> masterLocations)
        {
            double max = double.MaxValue;
            XYZ newLocation = null;
            foreach (XYZ loc in masterLocations)
            {
                double distance = location.DistanceTo(loc);
                if (distance < max)
                {
                    max = distance;
                    newLocation = loc;
                }
            }
            return newLocation;
        }

        private Viewport ClosestSlave(XYZ masterLocation, List<ElementId> slaveViewports)
        {
            double max = double.MaxValue;
            Viewport closest = null;
            foreach (ElementId id in slaveViewports)
            {
                Viewport viewport = _doc.GetElement(id) as Viewport;
                double distance = masterLocation.DistanceTo(viewport.GetBoxCenter());
                if (distance < max)
                {
                    max = distance;
                    closest = viewport;
                }
            }
            return closest;
        }
    }
}
