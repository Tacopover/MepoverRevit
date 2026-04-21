using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc2x3.GeometricConstraintResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.RepresentationResource;
using Xbim.Ifc2x3.UtilityResource;
using Xbim.IO;

namespace IfcExport
{
    /// <summary>
    /// Creates the IfcStore and the IFC project hierarchy
    /// (IfcProject ? IfcSite ? IfcBuilding ? one IfcBuildingStorey per Revit Level)
    /// at the start of a session. Populates the Xbim-typed fields on
    /// <see cref="IfcExportSession"/> so subsequent ticks can reference them.
    ///
    /// Must be called from a valid Revit API context (Idling handler).
    /// </summary>
    internal static class IfcProjectInitializer
    {
        private const double FeetToMetres = 0.3048;

        /// <summary>
        /// Initialises <paramref name="session"/> with a new IfcStore and full project hierarchy.
        /// </summary>
        /// <returns>True when complete; false to retry next tick.</returns>
        public static bool Initialize(IfcExportSession session, Document revitDoc)
        {
            try
            {
                var credentials = new XbimEditorCredentials
                {
                    ApplicationDevelopersName = "MEPover",
                    ApplicationFullName       = "MEPover IFC Exporter",
                    ApplicationIdentifier     = "MEPoverIFC",
                    ApplicationVersion        = "1.0",
                    EditorsFamilyName         = "MEPover",
                    EditorsGivenName          = "IFC",
                    EditorsOrganisationName   = "MEPover"
                };

                // Schema is chosen from the session setting; default to IFC2x3.
                XbimSchemaVersion schema = ParseSchema(session.IfcVersion);

                var store = IfcStore.Create(credentials, schema, XbimStoreType.InMemoryModel);
                session.Store = store;

                using (var txn = store.BeginTransaction("Initialise project hierarchy"))
                {
                    BuildHierarchy(store, session, revitDoc);
                    txn.Commit();
                }

                return true;
            }
            catch (Exception ex)
            {
                // Surface the error through the session so the orchestrator can report it.
                throw new InvalidOperationException("IfcProjectInitializer failed: " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ hierarchy builder

        private static void BuildHierarchy(IfcStore store, IfcExportSession session, Document doc)
        {
            var i = store.Instances;

            // IfcProject
            var project = i.New<IfcProject>(p =>
            {
                p.Name     = doc.Title ?? "Revit Project";
                p.GlobalId = NewGuid();
                p.LongName = doc.Title ?? "Revit Project";
            });

            // Units — metres (length) + radians (plane angle) are required for geometry.
            project.UnitsInContext = i.New<IfcUnitAssignment>(u =>
            {
                u.Units.Add(i.New<IfcSIUnit>(s =>
                {
                    s.UnitType = IfcUnitEnum.LENGTHUNIT;
                    s.Name     = IfcSIUnitName.METRE;
                }));
                u.Units.Add(i.New<IfcSIUnit>(s =>
                {
                    s.UnitType = IfcUnitEnum.PLANEANGLEUNIT;
                    s.Name     = IfcSIUnitName.RADIAN;
                }));
            });

            // Representation contexts — without these viewers reject the file as invalid.
            var modelContext = i.New<IfcGeometricRepresentationContext>(c =>
            {
                c.ContextType              = "Model";
                c.ContextIdentifier        = "Body";
                c.CoordinateSpaceDimension = 3;
                c.Precision                = 0.00001;
                c.WorldCoordinateSystem    = i.New<IfcAxis2Placement3D>(a =>
                    a.Location = i.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0)));
            });
            project.RepresentationContexts.Add(modelContext);
            project.RepresentationContexts.Add(i.New<IfcGeometricRepresentationContext>(c =>
            {
                c.ContextType              = "Plan";
                c.ContextIdentifier        = "Annotation";
                c.CoordinateSpaceDimension = 2;
                c.Precision                = 0.00001;
                c.WorldCoordinateSystem    = i.New<IfcAxis2Placement2D>(a =>
                    a.Location = i.New<IfcCartesianPoint>(p => p.SetXY(0, 0)));
            }));

            // Store the model context on the session — every element write references it.
            session.ModelContext = modelContext;

            // IfcSite ? IfcBuilding
            var site = i.New<IfcSite>(s =>
            {
                s.Name            = "Default Site";
                s.GlobalId        = NewGuid();
                s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                s.ObjectPlacement = WorldPlacement(store);
            });
            project.AddSite(site);

            var building = i.New<IfcBuilding>(b =>
            {
                b.Name            = doc.Title ?? "Building";
                b.GlobalId        = NewGuid();
                b.CompositionType = IfcElementCompositionEnum.ELEMENT;
                b.ObjectPlacement = WorldPlacement(store);
            });
            site.AddBuilding(building);

            // IfcBuildingStorey per Revit Level
            var buildingAgg = i.New<IfcRelAggregates>(r =>
            {
                r.GlobalId       = NewGuid();
                r.RelatingObject = building;
            });

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            foreach (Level level in levels)
            {
                double elevM = level.Elevation * FeetToMetres;
                var storey = i.New<IfcBuildingStorey>(s =>
                {
                    s.Name            = level.Name;
                    s.GlobalId        = NewGuid();
                    s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    s.Elevation       = elevM;
                    s.ObjectPlacement = ElevationPlacement(store, elevM);
                });
                buildingAgg.RelatedObjects.Add(storey);
                session.LevelMap[level.Id] = storey;
            }

            // Fallback storey for elements that have no level parameter.
            session.FallbackStorey = session.LevelMap.Count > 0
                ? session.LevelMap.Values.First()
                : i.New<IfcBuildingStorey>(s =>
                {
                    s.Name            = "Ground Floor";
                    s.GlobalId        = NewGuid();
                    s.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    s.Elevation       = 0;
                    s.ObjectPlacement = WorldPlacement(store);
                    buildingAgg.RelatedObjects.Add(s);
                });
        }

        // ------------------------------------------------------------------ helpers

        private static XbimSchemaVersion ParseSchema(string ifcVersion)
        {
            if (ifcVersion == "IFC4")   return XbimSchemaVersion.Ifc4;
            if (ifcVersion == "IFC4.3") return XbimSchemaVersion.Ifc4x3;
            return XbimSchemaVersion.Ifc2X3;   // default
        }

        private static IfcLocalPlacement WorldPlacement(IfcStore store) =>
            store.Instances.New<IfcLocalPlacement>(lp =>
                lp.RelativePlacement = store.Instances.New<IfcAxis2Placement3D>(a =>
                    a.Location = store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0))));

        private static IfcLocalPlacement ElevationPlacement(IfcStore store, double elevM) =>
            store.Instances.New<IfcLocalPlacement>(lp =>
                lp.RelativePlacement = store.Instances.New<IfcAxis2Placement3D>(a =>
                    a.Location = store.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, elevM))));

        private static IfcGloballyUniqueId NewGuid() =>
            IfcGloballyUniqueId.ConvertToBase64(Guid.NewGuid());
    }
}
