using Autodesk.Revit.UI;
using System;

namespace IfcExport
{
    /// <summary>
    /// A single unit of deferred work consumed by the Idling handler.
    /// Follows the task-pump pattern described in the plan: each task is
    /// short, idempotent, and sets Completed = true on all terminal paths.
    /// </summary>
    internal class IdleTask
    {
        private readonly Func<UIApplication, bool> _callback;
        private readonly Func<bool> _readyCheck;
        private int _attempts;
        private readonly int _maxAttempts;

        /// <summary>Set to true by the callback on success, or by the pump when retries are exhausted.</summary>
        public bool Completed { get; set; }

        /// <param name="callback">Work to run. Return true when the task is done, false to retry next tick.</param>
        /// <param name="readyCheck">Optional guard — if null, always considered ready.</param>
        /// <param name="maxAttempts">0 = unlimited retries.</param>
        public IdleTask(Func<UIApplication, bool> callback, Func<bool> readyCheck = null, int maxAttempts = 0)
        {
            _callback = callback;
            _readyCheck = readyCheck;
            _maxAttempts = maxAttempts;
        }

        public bool IsReady()
        {
            if (_maxAttempts > 0 && _attempts >= _maxAttempts)
            {
                Completed = true;
                return false;
            }
            return _readyCheck == null || _readyCheck();
        }

        public void Eval(UIApplication uiApp)
        {
            _attempts++;
            Completed = _callback(uiApp);
        }
    }
}
