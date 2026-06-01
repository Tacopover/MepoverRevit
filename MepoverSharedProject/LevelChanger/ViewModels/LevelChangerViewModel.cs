using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject;
using MepoverSharedProject.Common;

namespace LevelChanger.ViewModels
{
    /// <summary>
    /// Reassigns MEP elements to the closest selected reference level (within a margin), keeping
    /// their absolute Z by adjusting the offset parameter.
    /// </summary>
    public class LevelChangerViewModel : ToolViewModel
    {
        private readonly Document _doc;
        private readonly List<ElementId> _preselectedIds;

        private static readonly BuiltInCategory[] MepCategories =
        {
            BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting, BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_TelephoneDevices, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_FlexPipeCurves
        };

        public ObservableCollection<LevelListItem> Levels { get; } = new ObservableCollection<LevelListItem>();

        public bool CanUseSelection { get; }

        private bool _isSelectionScope;
        public bool IsSelectionScope
        {
            get => _isSelectionScope;
            set { _isSelectionScope = value; OnPropertyChanged(nameof(IsSelectionScope)); OnPropertyChanged(nameof(IsModelScope)); }
        }

        public bool IsModelScope
        {
            get => !_isSelectionScope;
            set { _isSelectionScope = !value; OnPropertyChanged(nameof(IsSelectionScope)); OnPropertyChanged(nameof(IsModelScope)); }
        }

        private string _marginText = "300";
        public string MarginText
        {
            get => _marginText;
            set { _marginText = value; OnPropertyChanged(nameof(MarginText)); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public RelayCommand<object> ToggleAllCommand { get; }
        public RelayCommand<object> OkCommand { get; }
        public RelayCommand<object> CloseCommand { get; }

        public LevelChangerViewModel(UIDocument uidoc, ICollection<ElementId> selectedIds)
        {
            _doc = uidoc.Document;
            _preselectedIds = selectedIds != null ? selectedIds.ToList() : new List<ElementId>();
            CanUseSelection = _preselectedIds.Count > 0;
            _isSelectionScope = CanUseSelection;

            foreach (Level level in new FilteredElementCollector(_doc)
                         .OfClass(typeof(Level))
                         .Cast<Level>()
                         .OrderBy(l => l.Elevation))
            {
                Levels.Add(new LevelListItem(level));
            }

            ToggleAllCommand = new RelayCommand<object>(p => true, p => ToggleAll());
            OkCommand = new RelayCommand<object>(p => true, p => Apply());
            CloseCommand = new RelayCommand<object>(p => true, p => CloseDialog(false));
        }

        private void ToggleAll()
        {
            bool allChecked = Levels.All(l => l.IsChecked);
            foreach (LevelListItem item in Levels)
            {
                item.IsChecked = !allChecked;
            }
        }

        private void Apply()
        {
            List<Level> levels = Levels.Where(l => l.IsChecked).Select(l => l.Level).ToList();
            if (levels.Count == 0)
            {
                StatusMessage = "Select at least one reference level.";
                return;
            }

            if (!double.TryParse(MarginText, NumberStyles.Any, CultureInfo.CurrentCulture, out double marginMm) &&
                !double.TryParse(MarginText, NumberStyles.Any, CultureInfo.InvariantCulture, out marginMm))
            {
                StatusMessage = "Margin must be a number (mm).";
                return;
            }
            double margin = marginMm / 304.8;

            Dictionary<Level, double> levelDict = new Dictionary<Level, double>();
            Level lowestLevel = null;
            double minElevation = double.MaxValue;
            foreach (Level level in levels)
            {
                levelDict[level] = level.Elevation;
                if (level.Elevation < minElevation)
                {
                    minElevation = level.Elevation;
                    lowestLevel = level;
                }
            }

            List<Element> targets;
            if (IsModelScope)
            {
                ElementMulticategoryFilter filter = new ElementMulticategoryFilter(MepCategories);
                targets = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToList();
            }
            else
            {
                targets = _preselectedIds.Select(id => _doc.GetElement(id)).Where(e => e != null).ToList();
            }

            int changed = 0;
            int skipped = 0;
            using (Transaction trans = new Transaction(_doc, "MEPover: Set Levels"))
            {
                trans.Start();
                foreach (Element element in targets)
                {
                    if (SetElementOffset(element, levelDict, lowestLevel, margin))
                    {
                        changed++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                trans.Commit();
            }

            StatusMessage = $"{changed} element(s) updated" + (skipped > 0 ? $", {skipped} skipped." : ".");
            CloseDialog(true);
        }

        // Returns true when the element's level was evaluated (whether or not it moved), false when
        // the element has neither a family-level nor an MEP start-level parameter.
        private bool SetElementOffset(Element element, Dictionary<Level, double> levelDict, Level lowestLevel, double margin)
        {
            Parameter levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            Parameter offsetParam = element.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
            if (levelParam != null && offsetParam != null &&
                _doc.GetElement(levelParam.AsElementId()) is Level currentLevel)
            {
                double offset = offsetParam.AsDouble();
                double z = offset + currentLevel.Elevation + margin;
                Level refLevel = ClosestLevel(z, levelDict, lowestLevel);
                if (refLevel != null && refLevel.Id != currentLevel.Id)
                {
                    double newOffset = offset + currentLevel.Elevation - refLevel.Elevation;
                    levelParam.Set(refLevel.Id);
                    offsetParam.Set(newOffset);
                }
                return true;
            }

            // MEP curves (pipes/ducts/conduits) use the run start level + offset instead.
            Parameter startLevelParam = element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            Parameter rbsOffsetParam = element.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
            if (startLevelParam != null && rbsOffsetParam != null &&
                _doc.GetElement(startLevelParam.AsElementId()) is Level startLevel)
            {
                double offset = rbsOffsetParam.AsDouble();
                double z = offset + startLevel.Elevation + margin;
                Level refLevel = ClosestLevel(z, levelDict, lowestLevel);
                if (refLevel != null && refLevel.Id != startLevel.Id)
                {
                    startLevelParam.Set(refLevel.Id);
                }
                return true;
            }

            return false;
        }

        // Closest level at or below the given Z; falls back to the lowest selected level.
        private static Level ClosestLevel(double z, Dictionary<Level, double> levelDict, Level lowestLevel)
        {
            Level level = null;
            double min = double.MaxValue;
            foreach (KeyValuePair<Level, double> pair in levelDict)
            {
                if (pair.Value > z)
                {
                    continue;
                }
                double distance = Math.Abs(z - pair.Value);
                if (distance < min)
                {
                    min = distance;
                    level = pair.Key;
                }
            }
            return level ?? lowestLevel;
        }
    }
}
