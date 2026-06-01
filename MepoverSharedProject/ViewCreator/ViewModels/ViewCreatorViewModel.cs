using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject;
using MepoverSharedProject.Common;
using View = Autodesk.Revit.DB.View;

namespace ViewCreator.ViewModels
{
    /// <summary>
    /// Bulk-creates floor or ceiling plan views as the Cartesian product of the checked view
    /// templates and levels. Names them "&lt;level&gt; - &lt;template&gt;".
    /// </summary>
    public class ViewCreatorViewModel : ToolViewModel
    {
        private readonly Document _doc;

        public ObservableCollection<ViewCreateItem> Templates { get; } = new ObservableCollection<ViewCreateItem>();
        public ObservableCollection<ViewCreateItem> Levels { get; } = new ObservableCollection<ViewCreateItem>();

        private bool _isFloorPlan = true;
        public bool IsFloorPlan
        {
            get => _isFloorPlan;
            set
            {
                _isFloorPlan = value;
                OnPropertyChanged(nameof(IsFloorPlan));
                OnPropertyChanged(nameof(IsCeilingPlan));
            }
        }

        public bool IsCeilingPlan
        {
            get => !_isFloorPlan;
            set
            {
                _isFloorPlan = !value;
                OnPropertyChanged(nameof(IsFloorPlan));
                OnPropertyChanged(nameof(IsCeilingPlan));
            }
        }

        private bool _useLevelNumbers = true;
        public bool UseLevelNumbers
        {
            get => _useLevelNumbers;
            set { _useLevelNumbers = value; OnPropertyChanged(nameof(UseLevelNumbers)); UpdatePreview(); }
        }

        private string _preview;
        public string Preview
        {
            get => _preview;
            set { _preview = value; OnPropertyChanged(nameof(Preview)); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public RelayCommand<object> AllTemplatesCommand { get; }
        public RelayCommand<object> NoTemplatesCommand { get; }
        public RelayCommand<object> AllLevelsCommand { get; }
        public RelayCommand<object> NoLevelsCommand { get; }
        public RelayCommand<object> CreateCommand { get; }
        public RelayCommand<object> CloseCommand { get; }

        public ViewCreatorViewModel(UIDocument uidoc)
        {
            _doc = uidoc.Document;

            foreach (View template in new FilteredElementCollector(_doc)
                         .OfCategory(BuiltInCategory.OST_Views)
                         .Cast<View>()
                         .Where(v => v.IsTemplate)
                         .OrderBy(v => v.Name))
            {
                Templates.Add(Track(new ViewCreateItem(template, template.Name)));
            }

            foreach (Level level in new FilteredElementCollector(_doc)
                         .OfClass(typeof(Level))
                         .Cast<Level>()
                         .OrderBy(l => l.Name))
            {
                Levels.Add(Track(new ViewCreateItem(level, level.Name)));
            }

            AllTemplatesCommand = new RelayCommand<object>(p => true, p => SetAll(Templates, true));
            NoTemplatesCommand = new RelayCommand<object>(p => true, p => SetAll(Templates, false));
            AllLevelsCommand = new RelayCommand<object>(p => true, p => SetAll(Levels, true));
            NoLevelsCommand = new RelayCommand<object>(p => true, p => SetAll(Levels, false));
            CreateCommand = new RelayCommand<object>(p => true, p => Create());
            CloseCommand = new RelayCommand<object>(p => true, p => CloseDialog(false));

            UpdatePreview();
        }

        private ViewCreateItem Track(ViewCreateItem item)
        {
            item.PropertyChanged += OnItemChanged;
            return item;
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewCreateItem.IsChecked))
            {
                UpdatePreview();
            }
        }

        private static void SetAll(IEnumerable<ViewCreateItem> items, bool value)
        {
            foreach (ViewCreateItem item in items)
            {
                item.IsChecked = value;
            }
        }

        private void UpdatePreview()
        {
            ViewCreateItem template = Templates.FirstOrDefault(t => t.IsChecked) ?? Templates.FirstOrDefault();
            ViewCreateItem level = Levels.FirstOrDefault(l => l.IsChecked) ?? Levels.FirstOrDefault();
            string levelToken = level == null
                ? "<level>"
                : (UseLevelNumbers ? GetLevelNum(level.Name) : level.Name);
            string templateName = template?.Name ?? "<template>";
            Preview = levelToken + " - " + templateName;
        }

        private void Create()
        {
            List<ViewCreateItem> templates = Templates.Where(t => t.IsChecked).ToList();
            List<ViewCreateItem> levels = Levels.Where(l => l.IsChecked).ToList();

            if (templates.Count == 0 || levels.Count == 0)
            {
                StatusMessage = "Select at least one view template and one level.";
                return;
            }

            ViewFamily targetFamily = IsFloorPlan ? ViewFamily.FloorPlan : ViewFamily.CeilingPlan;
            ViewFamilyType viewFamilyType = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == targetFamily);

            if (viewFamilyType == null)
            {
                StatusMessage = $"No {(IsFloorPlan ? "floor" : "ceiling")} plan view type exists in this project.";
                return;
            }

            int created = 0;
            int skipped = 0;
            int copyCounter = 0;

            using (Transaction trans = new Transaction(_doc, "MEPover: Create Views"))
            {
                trans.Start();
                foreach (ViewCreateItem level in levels)
                {
                    foreach (ViewCreateItem template in templates)
                    {
                        ViewPlan newView = ViewPlan.Create(_doc, viewFamilyType.Id, level.Element.Id);
                        string baseName = (UseLevelNumbers ? GetLevelNum(level.Name) : level.Name) + " - " + template.Name;
                        if (TrySetName(newView, baseName, ref copyCounter))
                        {
                            newView.ViewTemplateId = template.Element.Id;
                            created++;
                        }
                        else
                        {
                            _doc.Delete(newView.Id);
                            skipped++;
                        }
                    }
                }
                trans.Commit();
            }

            StatusMessage = skipped == 0
                ? $"{created} view(s) created."
                : $"{created} view(s) created, {skipped} skipped (duplicate names).";
        }

        // Tries the preferred name, then a "_copy" variant, before giving up.
        private static bool TrySetName(View view, string baseName, ref int copyCounter)
        {
            try
            {
                view.Name = baseName;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                try
                {
                    view.Name = baseName + "_copy" + copyCounter++;
                    return true;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    return false;
                }
            }
        }

        // Extracts the leading numeric token from a level name (e.g. "L02 - Ground" -> "02").
        private static string GetLevelNum(string levelName)
        {
            string levelNum = string.Empty;
            const string escapes = @"-\_ ";
            int start = 0;
            for (int i = 0; i < levelName.Length; i++)
            {
                if (char.IsDigit(levelName, i) || levelName[i].Equals('-'))
                {
                    start = 1;
                }
                if (start == 0)
                {
                    continue;
                }
                if (escapes.Contains(levelName[i]) && i > 0)
                {
                    break;
                }
                levelNum += levelName[i];
            }
            return string.IsNullOrEmpty(levelNum) ? levelName : levelNum;
        }
    }
}
