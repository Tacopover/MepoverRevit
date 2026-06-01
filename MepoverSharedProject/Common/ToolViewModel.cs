using System;

namespace MepoverSharedProject.Common
{
    /// <summary>
    /// Base ViewModel for the modal MEPover tool dialogs. Adds a close channel on top of
    /// <see cref="BaseViewModel"/> so command handlers can dismiss the hosting window.
    /// </summary>
    public abstract class ToolViewModel : BaseViewModel, IClosableViewModel
    {
        public event Action<bool?> RequestClose;

        /// <summary>Closes the hosting dialog. true = OK/accepted, false/null = cancelled.</summary>
        protected void CloseDialog(bool? result)
        {
            RequestClose?.Invoke(result);
        }
    }
}
