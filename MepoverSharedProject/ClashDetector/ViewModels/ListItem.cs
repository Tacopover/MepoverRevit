using MepoverSharedProject;
using System;
using System.Collections.Generic;
using System.Text;

namespace ClashDetector.ViewModels
{
    public class ListItem : BaseViewModel
    {
        public string Name { get; set; }

        private bool isSelected;
        public bool IsSelected
        {
            get { return isSelected; }
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public ListItem(string name, bool selected = false)
        {
            Name = name;
            IsSelected = selected;
        }
    }
}
