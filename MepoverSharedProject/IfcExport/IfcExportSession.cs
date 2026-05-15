using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using Xbim.Ifc;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.RepresentationResource;

namespace IfcExport
{
    /// <summary>
    /// Holds all mutable state for a single export session.
    /// Lives for the lifetime of one Run ? (Pause/Resume)* ? Completed/Cancelled cycle.
    /// All Xbim references here are valid as long as <see cref="Store"/> is not disposed.
    /// </summary>
    internal class IfcExportSession : IDisposable
    {
        // ------------------------------------------------------------------ element queue / progress
        public Queue<ElementId> ElementQueue   { get; } = new Queue<ElementId>();
        public int TotalElements  { get; set; }
        public int ExportedElements { get; set; }

        // ------------------------------------------------------------------ change-tracking maps (Phase 3)
        /// <summary>Revit UniqueId ? IFC GUID. Serialised to JSON sidecar in Phase 5.</summary>
        public Dictionary<string, string>     ExportStateMap { get; } = new Dictionary<string, string>();
        /// <summary>ElementId ? UniqueId reverse index. Rebuilt from document on resume.</summary>
        public Dictionary<ElementId, string>  ElementIdIndex { get; } = new Dictionary<ElementId, string>();

        // ------------------------------------------------------------------ Xbim store
        /// <summary>The live in-memory IFC model. Kept open between idle ticks.</summary>
        public IfcStore Store { get; set; }

        /// <summary>
        /// The 3-D "Model" representation context created during initialisation.
        /// Every element's IfcShapeRepresentation must reference this context.
        /// </summary>
        public IfcGeometricRepresentationContext ModelContext { get; set; }

        /// <summary>
        /// Maps Revit Level ElementId ? the corresponding IfcBuildingStorey.
        /// Populated by <see cref="IfcProjectInitializer"/>.
        /// </summary>
        public Dictionary<ElementId, IfcBuildingStorey> LevelMap { get; }
            = new Dictionary<ElementId, IfcBuildingStorey>();

        /// <summary>
        /// Maps IfcBuildingStorey ? the IfcRelContainedInSpatialStructure that collects
        /// elements for that storey. Created lazily by <see cref="IfcElementWriter"/>.
        /// </summary>
        public Dictionary<IfcBuildingStorey, IfcRelContainedInSpatialStructure> ContainsMap { get; }
            = new Dictionary<IfcBuildingStorey, IfcRelContainedInSpatialStructure>();

        /// <summary>Storey used when an element has no recognisable level parameter.</summary>
        public IfcBuildingStorey FallbackStorey { get; set; }

        // ------------------------------------------------------------------ settings
        public string DestinationFolder  { get; set; }
        public string IfcVersion         { get; set; }
        public List<PsetMappingBlock> PsetMappings { get; set; } = new List<PsetMappingBlock>();

        // ------------------------------------------------------------------ change tracking (Phase 3)

        /// <summary>
        /// Changes queued by <c>DocumentChanged</c> for deferred processing.
        /// Pending changes are drained with priority over the initial export queue.
        /// </summary>
        public Queue<PendingChange> PendingChangeQueue { get; } = new Queue<PendingChange>();

        /// <summary>
        /// ElementIds currently in <see cref="PendingChangeQueue"/> as Modified changes.
        /// Used to skip re-queuing the same element (e.g. direct + indirect modification in one event).
        /// Entries are removed when the corresponding Modified change is processed.
        /// </summary>
        public HashSet<ElementId> PendingModifiedIds { get; } = new HashSet<ElementId>();

        /// <summary>UTC timestamp of the last <c>DocumentChanged</c> event; used for the 2-second quiet period.</summary>
        public DateTime LastDocumentChangedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Cached Revit document reference � set once the Collect task runs so the
        /// <c>DocumentChanged</c> handler and drain tasks do not need a UIApplication parameter.
        /// </summary>
        public Document RevitDocument { get; set; }

        /// <summary>True once the initial full element queue has been completely drained.</summary>
        public bool InitialExportComplete { get; set; }

        // ------------------------------------------------------------------ timing
        public DateTime LastSaveUtc   { get; set; } = DateTime.UtcNow;
        public DateTime LastChangeUtc { get; set; } = DateTime.MinValue;

        // ------------------------------------------------------------------ lifecycle
        public void Dispose()
        {
            Store?.Dispose();
            Store = null;
        }
    }
}
