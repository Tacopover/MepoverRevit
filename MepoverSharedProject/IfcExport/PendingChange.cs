using Autodesk.Revit.DB;

namespace IfcExport
{
    internal enum ChangeType
    {
        Added,
        Modified,
        Deleted
    }

    /// <summary>
    /// A single pending change queued by the <c>DocumentChanged</c> event handler
    /// for deferred processing in the Idling handler.
    ///
    /// Deleted changes carry a <see cref="UniqueId"/> resolved at event-fire time
    /// from <see cref="IfcExportSession.ElementIdIndex"/> before the element
    /// disappears from the document.  Added / Modified changes resolve UniqueId
    /// lazily when the change is actually processed.
    /// </summary>
    internal class PendingChange
    {
        public ChangeType Type      { get; }
        public ElementId  ElementId { get; }
        /// <summary>
        /// Pre-resolved UniqueId for Deleted changes; null for Added / Modified.
        /// </summary>
        public string     UniqueId  { get; }

        public PendingChange(ChangeType type, ElementId elementId, string uniqueId = null)
        {
            Type      = type;
            ElementId = elementId;
            UniqueId  = uniqueId;
        }
    }
}
