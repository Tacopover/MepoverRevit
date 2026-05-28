using MepoverSharedProject;
using System;
using System.Collections.Generic;
using System.Text;

namespace ClashDetector.ViewModels
{
    public class HostLinkViewModel : BaseViewModel
    {
        private ClashSettings _clashSettings;
        public ClashSettings ClashSettings
        {
            get => _clashSettings;
            set
            {
                _clashSettings = value;
                OnPropertyChanged(nameof(ClashSettings));
            }
        }
        public HostLinkViewModel(ClashSettings settings)
        {
            ClashSettings = settings;
        }

    }
}
