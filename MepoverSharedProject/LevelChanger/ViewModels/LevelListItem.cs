using Autodesk.Revit.DB;
using MepoverSharedProject;

namespace LevelChanger.ViewModels
{
    /// <summary>A checkable level row (name + elevation in mm) in the Level Changer dialog.</summary>
    public class LevelListItem : BaseViewModel
    {
        public Level Level { get; }
        public string Name { get; }
        public string ElevationMm { get; }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }

        public LevelListItem(Level level)
        {
            Level = level;
            Name = level.Name;
            ElevationMm = System.Math.Round(level.Elevation * 304.8).ToString("0");
        }
    }
}
