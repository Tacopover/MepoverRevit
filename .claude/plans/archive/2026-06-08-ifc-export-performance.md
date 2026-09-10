# IFC Export Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Speed up the initial IFC export by (A) processing multiple elements per Revit idle tick and (B) overlapping Revit geometry extraction with background Xbim writing via a producer-consumer pipeline.

**Architecture:**
Part A changes only `IdleExportOrchestrator.EnqueueDrainTask` — loops with an 80ms time budget instead of processing one element per tick. Part B splits `IfcElementWriter.WriteElement` into `ExtractDto` (Revit API, main thread) and `WriteElementFromDto` (Xbim only, background thread). A `BlockingCollection<ElementGeometryDto>` bridges them. The background thread owns all Xbim writes during initial export; pending document changes are deferred until the background thread completes to avoid store contention.

**Tech Stack:** C# 8, Revit API, Xbim.Essentials v6, `System.Collections.Concurrent.BlockingCollection`, `System.Threading`

---

## File Map

**Part A — one file:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

**Part B — four files:**
- Create: `MepoverSharedProject/IfcExport/ElementGeometryDto.cs`
- Modify: `MepoverSharedProject/IfcExport/IfcElementWriter.cs`
- Modify: `MepoverSharedProject/IfcExport/IfcExportSession.cs`
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`
- Modify: `MepoverSharedProject/MepoverSharedProject.projitems` (register new file)

---

## PART A — Batch Processing

### Task A1: Replace single-element drain with time-budgeted loop

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Add `BatchBudgetMs` constant to the class**

Add alongside the other constants at the top of `IdleExportOrchestrator` (near `SaveIntervalSeconds`):
```csharp
private const int BatchBudgetMs = 80;
```

- [ ] **Step 2: Replace the single-element block in `EnqueueDrainTask`**

In `EnqueueDrainTask` (around line 431), find the `else if (_session.ElementQueue.Count > 0)` block and replace it:

**Before:**
```csharp
else if (_session.ElementQueue.Count > 0)
{
    ElementId id = _session.ElementQueue.Dequeue();
    Element elem = doc.GetElement(id);
    if (elem != null)
    {
        IfcElementWriter.WriteElement(elem, _session);
        _session.ExportedElements++;
        _viewModel.ProgressValue = _session.ExportedElements;
        _viewModel.StatusText = string.Format(
            "Exporting element {0} of {1} — {2}",
            _session.ExportedElements,
            _session.TotalElements,
            elem.Name ?? string.Empty);
    }
    moreWork = (_session.ElementQueue.Count > 0
             || _session.PendingChangeQueue.Count > 0);
}
```

**After:**
```csharp
else if (_session.ElementQueue.Count > 0)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (_session.ElementQueue.Count > 0 && sw.ElapsedMilliseconds < BatchBudgetMs)
    {
        ElementId id = _session.ElementQueue.Dequeue();
        Element elem = doc.GetElement(id);
        if (elem != null)
        {
            IfcElementWriter.WriteElement(elem, _session);
            _session.ExportedElements++;
        }
    }
    _viewModel.ProgressValue = _session.ExportedElements;
    _viewModel.StatusText = string.Format(
        "Exporting {0} of {1}…",
        _session.ExportedElements,
        _session.TotalElements);
    moreWork = (_session.ElementQueue.Count > 0
             || _session.PendingChangeQueue.Count > 0);
}
```

- [ ] **Step 3: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**
```
git add MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs
git commit -m "perf(ifc-export): batch multiple elements per idle tick with 80ms time budget"
```

### Task A2: Manual verification

- [ ] Load plugin in Revit. Open a model with 500+ elements. Start IFC export. Observe:
  - Progress bar advances in larger steps (multiple elements per update tick)
  - Revit UI remains responsive during export
  - Output `.ifc` file is valid (open in any IFC viewer)

---

## PART B — Producer-Consumer Pipeline

### Task B1: Cache output paths in `IfcExportSession`

The background writer thread cannot call `uiApp.ActiveUIDocument.Document.Title` (Revit API, main thread only). Cache the resolved output file paths in the session during initialisation so the background thread can save without touching Revit.

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcExportSession.cs`
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Add output path properties to `IfcExportSession`**

In `IfcExportSession.cs`, add after the `DestinationFolder` / `IfcVersion` / `PsetMappings` properties:
```csharp
// ------------------------------------------------------------------ cached output paths
/// <summary>Absolute path of the final .ifc output file. Set once during the init task.</summary>
public string OutputFilePath { get; set; }
/// <summary>Absolute path of the atomic-swap temp file. Set once during the init task.</summary>
public string OutputTempFilePath { get; set; }
```

- [ ] **Step 2: Clear the new properties in `ResetForFullReExport()`**

In `IfcExportSession.cs`, add to the body of `ResetForFullReExport()`:
```csharp
OutputFilePath     = null;
OutputTempFilePath = null;
```

- [ ] **Step 3: Set the paths inside `EnqueueInitTask`**

In `IdleExportOrchestrator.cs`, inside the `EnqueueInitTask` `IdleTask` callback, after `Document doc = uiApp.ActiveUIDocument?.Document;` and the `if (doc == null) return true;` guard, add before `bool done = IfcProjectInitializer.Initialize(...)`:
```csharp
if (string.IsNullOrEmpty(_session.OutputFilePath))
{
    string safeName = MakeSafeFileName(
        System.IO.Path.GetFileNameWithoutExtension(doc.Title ?? "export"));
    _session.OutputFilePath     = System.IO.Path.Combine(_session.DestinationFolder, safeName + ".ifc");
    _session.OutputTempFilePath = System.IO.Path.Combine(_session.DestinationFolder, safeName + "_partial.ifc");
}
```

- [ ] **Step 4: Simplify `EnqueueSaveTask` to use the cached paths**

In `EnqueueSaveTask`, find the path-building block and replace it:

**Before** (inside the `IdleTask` lambda):
```csharp
string folder = _session.DestinationFolder;
string docTitle = uiApp.ActiveUIDocument?.Document?.Title ?? "export";
string safeName = MakeSafeFileName(System.IO.Path.GetFileNameWithoutExtension(docTitle));
string target = System.IO.Path.Combine(folder, safeName + ".ifc");
string temp = System.IO.Path.Combine(folder, safeName + "_partial.ifc");
```

**After:**
```csharp
string target = _session.OutputFilePath;
string temp   = _session.OutputTempFilePath;
if (string.IsNullOrEmpty(target)) return true;
```

- [ ] **Step 5: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**
```
git add MepoverSharedProject/IfcExport/IfcExportSession.cs MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs
git commit -m "refactor(ifc-export): cache output file paths in session so background thread can save without Revit API"
```

---

### Task B2: Create `ElementGeometryDto` types

New file holds all data needed to write one IFC element with zero Revit API calls.

**Files:**
- Create: `MepoverSharedProject/IfcExport/ElementGeometryDto.cs`
- Modify: `MepoverSharedProject/MepoverSharedProject.projitems`

- [ ] **Step 1: Create `ElementGeometryDto.cs`**

```csharp
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

        /// <summary>Level ElementId for storey lookup; null if the element has no level parameter.</summary>
        public ElementId LevelId { get; set; }

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
```

- [ ] **Step 2: Register the new file in `MepoverSharedProject.projitems`**

In `MepoverSharedProject.projitems`, after the line for `IfcElementWriter.cs` (around line 15), add:
```xml
<Compile Include="$(MSBuildThisFileDirectory)IfcExport\ElementGeometryDto.cs" />
```

- [ ] **Step 3: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**
```
git add MepoverSharedProject/IfcExport/ElementGeometryDto.cs MepoverSharedProject/MepoverSharedProject.projitems
git commit -m "feat(ifc-export): add ElementGeometryDto types for producer-consumer pipeline"
```

---

### Task B3: Add `ExtractDto` to `IfcElementWriter`

Extracts all Revit API data into a plain DTO. Must only be called on the Revit main thread.

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcElementWriter.cs`

- [ ] **Step 1: Add `ExtractDto` and `ExtractPsetData` after the existing `WriteElement` method**

Insert after the closing brace of `WriteElement` (before the `// --- geometry extraction` comment, around line 129):

```csharp
// ------------------------------------------------------------------ extraction (Revit main thread only)

/// <summary>
/// Reads all Revit API data needed to write an IFC element and packages it into a plain DTO.
/// Returns null when the element has no exportable solid geometry or on any error.
/// MUST be called on the Revit main thread — uses get_Geometry and face.Triangulate.
/// The returned DTO contains no Revit API objects and is safe to enqueue for background processing.
/// </summary>
public static ElementGeometryDto ExtractDto(Element element, IList<PsetMappingBlock> psetMappings)
{
    if (element == null) return null;
    try
    {
        List<Solid> solids = ExtractSolids(element);
        if (solids.Count == 0) return null;

        var solidData = new List<List<Triangle3d>>();
        foreach (Solid solid in solids)
        {
            var triangles = new List<Triangle3d>();
            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null || mesh.NumTriangles == 0) continue;
                for (int t = 0; t < mesh.NumTriangles; t++)
                {
                    MeshTriangle tri = mesh.get_Triangle(t);
                    XYZ v0 = tri.get_Vertex(0), v1 = tri.get_Vertex(1), v2 = tri.get_Vertex(2);
                    triangles.Add(new Triangle3d(
                        v0.X * FeetToMetres, v0.Y * FeetToMetres, v0.Z * FeetToMetres,
                        v1.X * FeetToMetres, v1.Y * FeetToMetres, v1.Z * FeetToMetres,
                        v2.X * FeetToMetres, v2.Y * FeetToMetres, v2.Z * FeetToMetres));
                }
            }
            if (triangles.Count > 0)
                solidData.Add(triangles);
        }
        if (solidData.Count == 0) return null;

        return new ElementGeometryDto
        {
            UniqueId      = element.UniqueId,
            ElementId     = element.Id,
            Name          = element.Name ?? element.Category?.Name ?? "Element",
            CategoryId    = element.Category?.Id?.IntegerValue ?? 0,
            LevelId       = GetLevelId(element),
            ExportToIfcAs = GetExportToIfcAs(element),
            Solids        = solidData,
            PropertySets  = ExtractPsetData(element, psetMappings)
        };
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            string.Format("IfcElementWriter.ExtractDto skipped '{0}' (id {1}): {2}",
                element?.Name, element?.Id, ex.Message));
        return null;
    }
}

private static List<PsetData> ExtractPsetData(Element element, IList<PsetMappingBlock> mappings)
{
    var result = new List<PsetData>();
    if (mappings == null || mappings.Count == 0) return result;

    foreach (PsetMappingBlock block in mappings)
    {
        var resolved   = new System.Collections.Generic.HashSet<string>();
        var properties = new List<PropertyData>();

        foreach (PsetPropertyMapping mapping in block.Properties)
        {
            if (resolved.Contains(mapping.IfcPropertyName)) continue;
            string value = TryGetRevitParameterValue(element, mapping.RevitParameterName);
            if (string.IsNullOrEmpty(value)) continue;
            resolved.Add(mapping.IfcPropertyName);
            properties.Add(new PropertyData
            {
                IfcPropertyName = mapping.IfcPropertyName,
                DataType        = mapping.DataType,
                Value           = value
            });
        }

        if (properties.Count > 0)
            result.Add(new PsetData
            {
                PsetName       = block.PsetName,
                IfcTypeFilters = new List<string>(block.IfcTypeFilters),
                Properties     = properties
            });
    }
    return result;
}
```

- [ ] **Step 2: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**
```
git add MepoverSharedProject/IfcExport/IfcElementWriter.cs
git commit -m "feat(ifc-export): add ExtractDto — separates Revit API reads from Xbim writes"
```

---

### Task B4: Add `WriteElementFromDto` and helpers to `IfcElementWriter`

Mirror of `WriteElement` that operates entirely on DTO data — no Revit API, safe to call from any thread.

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IfcElementWriter.cs`

- [ ] **Step 1: Add `WriteElementFromDto` public static method**

Insert after `ExtractPsetData` (still before the `// --- geometry extraction` comment):

```csharp
// ------------------------------------------------------------------ Xbim write (any thread, no Revit API)

/// <summary>
/// Writes a pre-extracted <see cref="ElementGeometryDto"/> into <paramref name="session"/>.Store.
/// No Revit API calls — safe to call from a background thread.
/// Updates ExportStateMap and ElementIdIndex on success.
/// </summary>
/// <returns>The IFC GUID assigned, or null if the element was skipped.</returns>
public static string WriteElementFromDto(ElementGeometryDto dto, IfcExportSession session)
{
    if (dto == null || session?.Store == null) return null;
    try
    {
        string ifcGuid;
        using (var txn = session.Store.BeginTransaction("WriteElement"))
        {
            var i = session.Store.Instances;

            var breps = new List<IfcFacetedBrep>();
            foreach (List<Triangle3d> solidTriangles in dto.Solids)
            {
                IfcFacetedBrep brep = TryCreateFacetedBrepFromTriangles(session.Store, solidTriangles);
                if (brep != null) breps.Add(brep);
            }
            if (breps.Count == 0) { txn.RollBack(); return null; }

            var shape = i.New<IfcShapeRepresentation>(s =>
            {
                s.ContextOfItems           = session.ModelContext;
                s.RepresentationIdentifier = "Body";
                s.RepresentationType       = "Brep";
            });
            foreach (var brep in breps) shape.Items.Add(brep);

            var guid         = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
            ifcGuid          = guid.ToString();
            var placement    = WorldPlacement(session.Store);
            var productShape = i.New<IfcProductDefinitionShape>(r => r.Representations.Add(shape));

            IfcElement entity = CreateEntityFromDto(session.Store, dto, guid, placement, productShape);
            AttachPropertySetsFromDto(session.Store, entity, dto.PropertySets);

            IfcBuildingStorey storey = ResolveStoreyFromDto(dto, session);
            if (!session.ContainsMap.TryGetValue(storey, out IfcRelContainedInSpatialStructure rel))
            {
                rel = i.New<IfcRelContainedInSpatialStructure>(r =>
                {
                    r.GlobalId          = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
                    r.RelatingStructure = storey;
                });
                session.ContainsMap[storey] = rel;
            }
            rel.RelatedElements.Add(entity);
            txn.Commit();
        }

        session.ExportStateMap[dto.UniqueId]  = ifcGuid;
        session.ElementIdIndex[dto.ElementId] = dto.UniqueId;
        return ifcGuid;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            string.Format("IfcElementWriter.WriteElementFromDto failed for '{0}': {1}", dto?.Name, ex.Message));
        return null;
    }
}
```

- [ ] **Step 2: Add `TryCreateFacetedBrepFromTriangles` private helper**

Insert after the existing `TryCreateFacetedBrep(IfcStore, Solid)` method:

```csharp
private static IfcFacetedBrep TryCreateFacetedBrepFromTriangles(
    Xbim.Ifc.IfcStore store, List<Triangle3d> triangles)
{
    if (triangles == null || triangles.Count == 0) return null;

    var ifcFaces = new List<IfcFace>(triangles.Count);
    foreach (Triangle3d tri in triangles)
    {
        Triangle3d t = tri; // copy for lambda capture
        ifcFaces.Add(store.Instances.New<IfcFace>(f =>
            f.Bounds.Add(store.Instances.New<IfcFaceOuterBound>(b =>
            {
                b.Orientation = true;
                b.Bound = store.Instances.New<IfcPolyLoop>(loop =>
                {
                    loop.Polygon.Add(store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(t.X0, t.Y0, t.Z0)));
                    loop.Polygon.Add(store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(t.X1, t.Y1, t.Z1)));
                    loop.Polygon.Add(store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(t.X2, t.Y2, t.Z2)));
                });
            }))));
    }

    return store.Instances.New<IfcFacetedBrep>(brep =>
        brep.Outer = store.Instances.New<IfcClosedShell>(shell =>
        {
            foreach (var f in ifcFaces) shell.CfsFaces.Add(f);
        }));
}
```

- [ ] **Step 3: Add `CreateEntityFromDto` private helper**

Insert after the existing `CreateEntity` method:

```csharp
private static IfcElement CreateEntityFromDto(
    Xbim.Ifc.IfcStore store,
    ElementGeometryDto dto,
    IfcGloballyUniqueId guid,
    IfcLocalPlacement placement,
    IfcProductDefinitionShape productShape)
{
    IfcElement e;
    if (!string.IsNullOrEmpty(dto.ExportToIfcAs)
        && IfcTypeNameMap.TryGetValue(dto.ExportToIfcAs, out var namedFactory))
        e = namedFactory(store);
    else if (dto.CategoryId != 0
        && CategoryEntityMap.TryGetValue(dto.CategoryId, out var catFactory))
        e = catFactory(store);
    else
        e = store.Instances.New<IfcBuildingElementProxy>();

    e.Name            = dto.Name;
    e.GlobalId        = guid;
    e.ObjectPlacement = placement;
    e.Representation  = productShape;
    return e;
}
```

- [ ] **Step 4: Add `AttachPropertySetsFromDto` private helper**

Insert after the existing `AttachPropertySets` method:

```csharp
private static void AttachPropertySetsFromDto(
    Xbim.Ifc.IfcStore store,
    IfcElement entity,
    List<PsetData> propertySets)
{
    if (propertySets == null || propertySets.Count == 0) return;
    var i = store.Instances;

    foreach (PsetData psetData in propertySets)
    {
        if (!EntityMatchesFilter(entity, psetData.IfcTypeFilters)) continue;

        var pset = i.New<IfcPropertySet>(ps =>
        {
            ps.GlobalId = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
            ps.Name     = psetData.PsetName;
        });

        foreach (PropertyData prop in psetData.Properties)
        {
            string propName  = prop.IfcPropertyName;
            string dataType  = prop.DataType;
            string propValue = prop.Value;
            pset.HasProperties.Add(i.New<IfcPropertySingleValue>(p =>
            {
                p.Name         = propName;
                p.NominalValue = MakeIfcValue(dataType, propValue);
            }));
        }
        if (pset.HasProperties.Count == 0) continue;

        i.New<IfcRelDefinesByProperties>(r =>
        {
            r.GlobalId                   = IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
            r.RelatedObjects.Add(entity);
            r.RelatingPropertyDefinition = pset;
        });
    }
}
```

- [ ] **Step 5: Add `ResolveStoreyFromDto` private helper**

Insert after the existing `ResolveStorey` method:

```csharp
private static IfcBuildingStorey ResolveStoreyFromDto(ElementGeometryDto dto, IfcExportSession session)
{
    if (dto.LevelId != null && dto.LevelId != ElementId.InvalidElementId
        && session.LevelMap.TryGetValue(dto.LevelId, out IfcBuildingStorey storey))
        return storey;
    return session.FallbackStorey;
}
```

- [ ] **Step 6: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**
```
git add MepoverSharedProject/IfcExport/IfcElementWriter.cs
git commit -m "feat(ifc-export): add WriteElementFromDto and helpers — Xbim write path with no Revit API"
```

---

### Task B5: Add background writer thread infrastructure to `IdleExportOrchestrator`

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Add `using` directives**

Ensure these are at the top of `IdleExportOrchestrator.cs`:
```csharp
using System.Collections.Concurrent;
using System.Threading;
```

- [ ] **Step 2: Add background writer fields**

Add after the existing `_syncInProgress` / `_syncStartedUtc` fields:
```csharp
// ------------------------------------------------------------------ background writer (producer-consumer)
private BlockingCollection<ElementGeometryDto> _dtoQueue;
private Thread _writerThread;
private CancellationTokenSource _writerCts;
private int _backgroundWrittenCount; // written only by background thread via Interlocked
```

- [ ] **Step 3: Add `StartBackgroundWriter` method**

Add as a private method (after `EnqueueSaveTask` is a good location):
```csharp
private void StartBackgroundWriter()
{
    _writerCts              = new CancellationTokenSource();
    _dtoQueue               = new BlockingCollection<ElementGeometryDto>(boundedCapacity: 500);
    _backgroundWrittenCount = 0;

    _writerThread = new Thread(() => BackgroundWriterLoop(_writerCts.Token))
    {
        IsBackground = true,
        Name         = "IFC-Background-Writer"
    };
    _writerThread.Start();
}
```

- [ ] **Step 4: Add `StopBackgroundWriter` method**

```csharp
private void StopBackgroundWriter(bool waitForCompletion)
{
    if (_dtoQueue != null && !_dtoQueue.IsAddingCompleted)
        _dtoQueue.CompleteAdding();

    _writerCts?.Cancel();

    if (waitForCompletion)
        _writerThread?.Join(TimeSpan.FromSeconds(30));

    _dtoQueue?.Dispose();
    _dtoQueue     = null;
    _writerThread = null;
    _writerCts?.Dispose();
    _writerCts    = null;
}
```

- [ ] **Step 5: Add `SaveIfcInternal` method**

This replaces the inline path-building + save logic that was previously duplicated in `EnqueueSaveTask`. It uses the cached paths from the session so no Revit API is needed.

```csharp
private void SaveIfcInternal()
{
    if (_session?.Store == null) return;
    string target = _session.OutputFilePath;
    string temp   = _session.OutputTempFilePath;
    if (string.IsNullOrEmpty(target)) return;

    try
    {
        _session.Store.SaveAs(temp, Xbim.IO.StorageType.Ifc);
        if (System.IO.File.Exists(target))
            System.IO.File.Delete(target);
        System.IO.File.Move(temp, target);
        _session.LastSaveUtc = DateTime.UtcNow;

        if (_session.CentralFilePath != null)
        {
            try
            {
                _session.CentralFileTimestampAtLastSave =
                    System.IO.File.GetLastWriteTimeUtc(_session.CentralFilePath);
            }
            catch { }
        }
    }
    catch (Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            (Action)(() => _viewModel.StatusText = "Save failed: " + ex.Message));
        if (System.IO.File.Exists(temp))
            try { System.IO.File.Delete(temp); } catch { }
    }
}
```

- [ ] **Step 6: Add `BackgroundWriterLoop` method**

```csharp
private void BackgroundWriterLoop(CancellationToken ct)
{
    var lastSave = DateTime.UtcNow;
    try
    {
        foreach (ElementGeometryDto dto in _dtoQueue.GetConsumingEnumerable(ct))
        {
            string guid = IfcElementWriter.WriteElementFromDto(dto, _session);
            if (guid != null)
            {
                int count = Interlocked.Increment(ref _backgroundWrittenCount);
                int total = _session.TotalElements;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() =>
                    {
                        _viewModel.ProgressValue = count;
                        _viewModel.StatusText    = string.Format("Writing {0} of {1} elements…", count, total);
                    }));
            }

            if ((DateTime.UtcNow - lastSave).TotalSeconds >= SaveIntervalSeconds)
            {
                SaveIfcInternal();
                lastSave = DateTime.UtcNow;
            }
        }
    }
    catch (OperationCanceledException) { /* expected on Cancel/Pause */ }

    if (!ct.IsCancellationRequested)
    {
        // Queue fully drained — initial export complete.
        SaveIfcInternal();
        _session.ExportedElements      = _backgroundWrittenCount;
        _session.InitialExportComplete = true;

        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            (Action)(() =>
            {
                _viewModel.ProgressValue = _session.TotalElements;
                _viewModel.StatusText    = "Export complete — monitoring for changes.";
                _viewModel.OnOrchestratorStateChanged();

                // Kick off a drain task if document changes accumulated during initial export.
                if (_session?.PendingChangeQueue.Count > 0 && _session.RevitDocument != null)
                    EnqueueDrainTask(_session.RevitDocument);
            }));
    }
}
```

- [ ] **Step 7: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**
```
git add MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs
git commit -m "feat(ifc-export): add background writer thread infrastructure (BlockingCollection, SaveIfcInternal, writer loop)"
```

---

### Task B6: Wire `Start`, `Cancel`, drain task, and save task to the pipeline

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Call `StartBackgroundWriter` in `Start()`**

In `Start()`, after `_state = ExportState.Running;`, add:
```csharp
StartBackgroundWriter();
```

- [ ] **Step 2: Stop background writer in `Cancel()`**

In `Cancel()`, before `_session?.Dispose()`, add:
```csharp
StopBackgroundWriter(waitForCompletion: false);
```

- [ ] **Step 3: Replace the processing block in `EnqueueDrainTask`**

Replace the entire `bool moreWork; if (...pending...) { } else if (...queue...) { } else { return true; }` block with:

```csharp
bool moreWork;

// Pending changes: only process after initial export is complete.
// During initial export the background thread is the sole writer to the Xbim store.
if (_session.InitialExportComplete && _session.PendingChangeQueue.Count > 0)
{
    PendingChange change = _session.PendingChangeQueue.Dequeue();
    ProcessPendingChange(change, doc);
    _viewModel.PendingChanges = _session.PendingChangeQueue.Count;
    moreWork = (_session.PendingChangeQueue.Count > 0);
}
else if (_session.ElementQueue.Count > 0 && !_session.InitialExportComplete)
{
    // Extract a batch of DTOs within the time budget and hand them to the background writer.
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (_session.ElementQueue.Count > 0 && sw.ElapsedMilliseconds < BatchBudgetMs)
    {
        ElementId id   = _session.ElementQueue.Peek();
        Element   elem = doc.GetElement(id);
        if (elem != null)
        {
            var dto = IfcElementWriter.ExtractDto(elem, _session.PsetMappings);
            if (dto != null && _dtoQueue != null && !_dtoQueue.TryAdd(dto, 0))
                break; // DTO queue full — come back next tick
        }
        _session.ElementQueue.Dequeue();
    }

    if (_session.ElementQueue.Count == 0 && _dtoQueue != null && !_dtoQueue.IsAddingCompleted)
        _dtoQueue.CompleteAdding(); // signal background thread: no more DTOs

    // Keep drain alive: more elements to extract OR waiting for background thread to finish.
    moreWork = (_session.ElementQueue.Count > 0 || !_session.InitialExportComplete);
}
else if (!_session.InitialExportComplete)
{
    // All elements extracted; background thread still draining and writing.
    // Re-enqueue this drain task so it wakes up once InitialExportComplete is set.
    moreWork = true;
}
else
{
    return true; // Nothing to do.
}
```

- [ ] **Step 4: Guard periodic saves in the drain task**

Find the periodic-save block that follows the `moreWork` assignment in `EnqueueDrainTask`:
```csharp
if ((DateTime.UtcNow - _session.LastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
{
    EnqueueSaveTask();
    _session.LastSaveUtc = DateTime.UtcNow;
}
```

Wrap it with an `InitialExportComplete` guard so the main thread does not save while the background thread is writing:
```csharp
if (_session.InitialExportComplete
    && (DateTime.UtcNow - _session.LastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
{
    EnqueueSaveTask();
    _session.LastSaveUtc = DateTime.UtcNow;
}
```

- [ ] **Step 5: Update `EnqueueSaveTask` to use `SaveIfcInternal`**

The existing `EnqueueSaveTask` lambda body no longer needs to build paths or do file I/O itself — `SaveIfcInternal` handles all of that now (Task B1 step 4 already removed the path-building; complete the refactor):

Replace the full lambda body in `EnqueueSaveTask` with:
```csharp
_tasks.Add(new IdleTask(uiApp =>
{
    SaveIfcInternal();
    return true;
}));
```

- [ ] **Step 6: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**
```
git add MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs
git commit -m "feat(ifc-export): wire drain task to producer-consumer pipeline; defer pending changes until background writer completes"
```

---

### Task B7: Guard `ResetForFullReExport` against live background thread

When central-file staleness detection triggers a full re-export, the store is disposed. The background writer must be stopped first or it will crash writing to a disposed store.

**Files:**
- Modify: `MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs`

- [ ] **Step 1: Find the `ResetForFullReExport` call site**

Search `IdleExportOrchestrator.cs` for `ResetForFullReExport`. There should be one call site (inside the staleness-check handler). Before that call, add:
```csharp
// Stop the background writer before resetting — it holds live references to the store.
StopBackgroundWriter(waitForCompletion: true);
```

After the `ResetForFullReExport()` call (and after the init/collect tasks are re-enqueued), add:
```csharp
StartBackgroundWriter();
```

- [ ] **Step 2: Build**
```
dotnet build MepoverRevit.2025/MepoverRevit.2025.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**
```
git add MepoverSharedProject/IfcExport/IdleExportOrchestrator.cs
git commit -m "fix(ifc-export): stop background writer before ResetForFullReExport to prevent store-disposed crash"
```

---

### Task B8: Manual verification

No automated tests are feasible for the extraction path (requires live Revit objects). The Xbim write path (`WriteElementFromDto`) is Revit-free and could be unit tested in isolation with a real `IfcStore`; that test project is out of scope here.

- [ ] **Test 1 — Initial export speed**
  Load a model with 1,000+ elements. Start IFC export. Observe that the initial export is faster than Part A alone. Task Manager should show a second thread (`IFC-Background-Writer`) with measurable CPU usage.

- [ ] **Test 2 — File validity**
  Open the exported `.ifc` in an IFC viewer (IFC.js Viewer, BIM Collab Zoom, or Solibri). Verify all expected elements appear with correct geometry and property sets.

- [ ] **Test 3 — Cancel during initial export**
  Start export on a large model. Click Cancel after ~10 seconds. Verify: no crash, `_writerThread` exits cleanly, partial `.ifc` is either absent or structurally valid.

- [ ] **Test 4 — Pause during initial export**
  Start export, click Pause mid-way. Wait 10 seconds. Click Resume. Verify export continues and completes correctly.
  > Note: Pause stops the main-thread drain task (via `readyCheck`). The background writer will keep draining whatever is already in `_dtoQueue`. This is acceptable — the queue drains to empty, background thread waits for more DTOs, and when Resume triggers the drain task again it adds more DTOs.

- [ ] **Test 5 — Document changes during initial export**
  Start export on a large model. While export is running, move an element in Revit. After export completes and monitoring mode starts, verify the element appears updated in the IFC (pending change was picked up).

- [ ] **Test 6 — View export after initial export**
  After initial export completes, use the "Export Active View" button. Verify the selected view's elements are updated in the output `.ifc`.

- [ ] **Test 7 — Workshared model: central-file staleness reset**
  On a workshared model, let the initial export complete, then simulate a staleness trigger (advance the central file timestamp beyond the poll threshold). Verify the full re-export restarts correctly with no crash.
