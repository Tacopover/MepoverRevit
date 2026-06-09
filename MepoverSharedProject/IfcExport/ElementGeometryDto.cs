using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace IfcExport
{
    /// <summary>
    /// Plain-data snapshot of one Revit element's geometry and parameters.
    /// Produced on the Revit main thread by <see cref="IfcElementWriter.ExtractDto"/>;
    /// consumed by the background Xbim writer via <see cref="IfcElementWriter.WriteElementFromDto"/>.
    /// Contains no Revit API objects that require main-thread affinity — safe to hand off.
    /// (ElementId is a plain int wrapper; it is only used as a dictionary key, never passed back to Revit API.)
    /// </summary>
    internal class ElementGeometryDto
    {
        /// <summary>Revit element.UniqueId — stable string key for ExportStateMap.</summary>
        public string UniqueId { get; set; }

        /// <summary>Revit ElementId — used as dictionary key in ElementIdIndex.</summary>
        public ElementId ElementId { get; set; }

        public string Name { get; set; }
        public int CategoryId { get; set; }

        /// <summary>Revit Level integer ID for storey lookup; -1 if the element has no level parameter.</summary>
        public int LevelId { get; set; }

        /// <summary>Value of the "Export To IFC As" Revit parameter, or null.</summary>
        public string ExportToIfcAs { get; set; }

        /// <summary>
        /// One list of triangles per Solid extracted from the element.
        /// All vertex coordinates are already in metres (Revit feet multiplied by 0.3048 at extraction time).
        /// </summary>
        public List<List<Triangle3d>> Solids { get; set; }

        /// <summary>Property sets with resolved string values — no further Revit API reads needed.</summary>
        public List<PsetData> PropertySets { get; set; }
    }

    /// <summary>
    /// One triangle in world-space metres.
    /// Struct to keep GC pressure low when millions of triangles are in flight.
    /// </summary>
    internal readonly struct Triangle3d
    {
        public readonly double X0, Y0, Z0, X1, Y1, Z1, X2, Y2, Z2;

        public Triangle3d(
            double x0, double y0, double z0,
            double x1, double y1, double z1,
            double x2, double y2, double z2)
        {
            X0 = x0; Y0 = y0; Z0 = z0;
            X1 = x1; Y1 = y1; Z1 = z1;
            X2 = x2; Y2 = y2; Z2 = z2;
        }
    }

    /// <summary>One resolved property set block ready for Xbim insertion.</summary>
    internal class PsetData
    {
        public string PsetName { get; set; }

        /// <summary>
        /// Mirrors <see cref="PsetMappingBlock.IfcTypeFilters"/>.
        /// Used by <see cref="IfcElementWriter.WriteElementFromDto"/> to filter by IFC entity type.
        /// </summary>
        public List<string> IfcTypeFilters { get; set; }

        public List<PropertyData> Properties { get; set; }
    }

    /// <summary>One resolved property value ready for Xbim insertion.</summary>
    internal class PropertyData
    {
        public string IfcPropertyName { get; set; }
        public string DataType { get; set; }
        public string Value { get; set; }
    }
}
