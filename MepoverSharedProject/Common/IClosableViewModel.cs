using System;

namespace MepoverSharedProject.Common
{
    /// <summary>
    /// Implemented by ViewModels hosted in a <see cref="ToolWindow"/> so they can request
    /// the dialog to close with a given result without referencing the Window directly.
    /// </summary>
    public interface IClosableViewModel
    {
        event Action<bool?> RequestClose;
    }
}
