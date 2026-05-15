using System.Collections.Generic;
using System.IO;

namespace IfcExport
{
    internal class PsetPropertyMapping
    {
        public string IfcPropertyName    { get; set; }
        public string DataType           { get; set; }
        public string RevitParameterName { get; set; }
    }

    internal class PsetMappingBlock
    {
        public string                    PsetName      { get; set; }
        public List<string>              IfcTypeFilters { get; set; } = new List<string>();
        public List<PsetPropertyMapping> Properties    { get; set; } = new List<PsetPropertyMapping>();
    }

    /// <summary>
    /// Parses a tab-delimited custom property set mapping file.
    ///
    /// Format:
    ///   # comment line (skipped)
    ///   PropertySet: [tab] PsetName [tab] I|T [tab] IfcType1 IfcType2 ...
    ///   [tab] IfcPropertyName [tab] DataType [tab] RevitParameterName
    ///
    /// Multiple rows with the same IfcPropertyName under one PropertySet are a
    /// fallback chain — the first row whose Revit parameter has a non-empty value wins.
    /// The I/T column is ignored (both instance and type parameters are always checked).
    /// </summary>
    internal static class PsetMappingParser
    {
        public static List<PsetMappingBlock> Parse(string filePath)
        {
            var result = new List<PsetMappingBlock>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return result;

            PsetMappingBlock current = null;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                // Skip blank lines and comment lines (including commented-out property rows).
                string trimmed = rawLine.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;

                string[] cols = rawLine.Split('\t');

                if (cols[0] == "PropertySet:" && cols.Length >= 4)
                {
                    // cols: [0]="PropertySet:" [1]=name [2]=I|T (ignored) [3]=space-sep IFC types
                    current = new PsetMappingBlock { PsetName = cols[1].Trim() };
                    foreach (string part in cols[3].Trim().Split(' '))
                    {
                        string t = part.Trim();
                        if (t.Length > 0)
                            current.IfcTypeFilters.Add(t);
                    }
                    result.Add(current);
                    continue;
                }

                // Property row: first column empty, at least 4 columns.
                if (current != null && cols[0] == "" && cols.Length >= 4)
                {
                    string revitParam = cols[3].Trim();
                    if (revitParam.Length == 0) continue;

                    current.Properties.Add(new PsetPropertyMapping
                    {
                        IfcPropertyName    = cols[1].Trim(),
                        DataType           = cols[2].Trim(),
                        RevitParameterName = revitParam
                    });
                }
            }

            return result;
        }
    }
}
