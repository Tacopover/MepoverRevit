using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IfcExport
{
    public record IfcExportDocumentSettings(string DestinationFolder, string IfcVersion, string PsetMappingFilePath);

    public class IfcExportPersistenceService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mepover", "ifc-export-settings.json");

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public void SaveDocument(string title, string folder, string ifcVersion, string psetPath)
        {
            var store = ReadStore();
            store[title] = new StoredEntry { DestinationFolder = folder, IfcVersion = ifcVersion, PsetMappingFilePath = psetPath };
            WriteStore(store);
        }

        public void RemoveDocument(string title)
        {
            var store = ReadStore();
            if (!store.Remove(title)) return;
            WriteStore(store);
        }

        public bool TryGetSettings(string title, out IfcExportDocumentSettings settings)
        {
            settings = null;
            var store = ReadStore();
            if (!store.TryGetValue(title, out var entry)) return false;
            settings = new IfcExportDocumentSettings(
                entry.DestinationFolder ?? string.Empty,
                entry.IfcVersion ?? "IFC2x3",
                entry.PsetMappingFilePath ?? string.Empty);
            return true;
        }

        private static Dictionary<string, StoredEntry> ReadStore()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new Dictionary<string, StoredEntry>();
                var text = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, StoredEntry>>(text, _jsonOptions)
                    ?? new Dictionary<string, StoredEntry>();
            }
            catch
            {
                return new Dictionary<string, StoredEntry>();
            }
        }

        private static void WriteStore(Dictionary<string, StoredEntry> store)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(store, _jsonOptions));
        }

        private class StoredEntry
        {
            public string DestinationFolder { get; set; }
            public string IfcVersion { get; set; }
            public string PsetMappingFilePath { get; set; }
        }
    }
}
