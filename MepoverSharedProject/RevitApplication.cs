using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IfcExport;
using System;
using System.IO;
using System.Reflection;
using Utilities;

namespace MepoverSharedProject
{
    public class RevitApplication : IExternalApplication
    {
        public static System.Windows.Media.ImageSource Icon;

        void AddRibbonPanel(UIControlledApplication application)
        {
            const string Tab = "MEPover";
            try { application.CreateRibbonTab(Tab); }
            catch { /* tab already exists */ }

            string path = Assembly.GetExecutingAssembly().Location;
            var assembly = Assembly.GetExecutingAssembly();

            // ── Models panel ──────────────────────────────────────────────────
            RibbonPanel models = application.CreateRibbonPanel(Tab, "Models");

            var ifc = models.AddItem(new PushButtonData("IFCExport", "IFC\nExport", path, "IfcExport.IfcExportCommand")) as PushButton;
            ifc.ToolTip = "Start incremental IFC export during idle time";
            ifc.LargeImage = Utils.LoadEmbeddedImage(assembly, "IfcExport.png");

            var wsTest = models.AddItem(new PushButtonData("WsChangeTest", "WS Change\nTest", path, "IfcExport.WsChangeTestCommand")) as PushButton;
            wsTest.ToolTip = "Toggle worksharing change event monitor";

            var clash = models.AddItem(new PushButtonData("ClashDetector", "Clash\nDetector", path, "ClashDetector.ClashDetectorCommand")) as PushButton;
            clash.ToolTip = "Detect clashes between models visible in the active view";
            clash.LargeImage = Utils.LoadEmbeddedImage(assembly, "ClashDetector.png");

            // ── Sheets & Views panel ──────────────────────────────────────────
            RibbonPanel sheets = application.CreateRibbonPanel(Tab, "Sheets & Views");

            var sc = sheets.AddItem(new PushButtonData("SC", "Sheet\nCopier", path, "SheetCopier.RevitCommand")) as PushButton;
            sc.ToolTip = "Start SheetCopier";
            sc.LargeImage = Utils.LoadEmbeddedImage(assembly, "SheetCopier.png");

            var vc = sheets.AddItem(new PushButtonData("ViewCreator", "View\nCreator", path, "ViewCreator.ViewCreatorCommand")) as PushButton;
            vc.ToolTip = "Bulk-create floor/ceiling plan views from templates and levels";
            vc.LargeImage = Utils.LoadEmbeddedImage(assembly, "VC.png");

            var avp = sheets.AddItem(new PushButtonData("AlignViewports", "Align\nViewports", path, "AlignViewports.AlignViewportsCommand")) as PushButton;
            avp.ToolTip = "Copy the viewport layout of a master sheet onto slave sheets";
            avp.LargeImage = Utils.LoadEmbeddedImage(assembly, "VA.png");

            var avw = sheets.AddItem(new PushButtonData("AlignViews", "Align\nViews", path, "AlignViews.AlignViewsCommand")) as PushButton;
            avw.ToolTip = "Sync the zoom/pan rectangle of all open plan views to the active view";
            avw.LargeImage = Utils.LoadEmbeddedImage(assembly, "AV.png");

            // ── Elements panel ────────────────────────────────────────────────
            RibbonPanel elems = application.CreateRibbonPanel(Tab, "Elements");

            var lc = elems.AddItem(new PushButtonData("LevelChanger", "Level\nChanger", path, "LevelChanger.LevelChangerCommand")) as PushButton;
            lc.ToolTip = "Reassign MEP elements to the closest reference level";
            lc.LargeImage = Utils.LoadEmbeddedImage(assembly, "LevelChanger.png");

            var oi = elems.AddItem(new PushButtonData("OffsetIncrementer", "Offset\nIncrementer", path, "OffsetIncrementer.OffsetIncrementerCommand")) as PushButton;
            oi.ToolTip = "Add a fixed increment to the offset of selected MEP elements";
            oi.LargeImage = Utils.LoadEmbeddedImage(assembly, "OffIncrem.png");

            var sf = elems.AddItem(new PushButtonData("SelectionFilter", "Selection\nFilter", path, "SelectionFilter.SelectionFilterCommand")) as PushButton;
            sf.ToolTip = "Filter the current selection by a parameter value";
            sf.LargeImage = Utils.LoadEmbeddedImage(assembly, "SF.png");
        }

        public Result OnStartup(UIControlledApplication application)
        {
#if REVIT2025
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
#endif
            try
            {
                AddRibbonPanel(application);
                IfcExportCommand.InitializeForAutoStart(application, new IfcExportPersistenceService());
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

#if REVIT2025
        private static Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string assemblyName = new AssemblyName(args.Name).Name;
            string dllPath = Path.Combine(pluginDir, assemblyName + ".dll");
            return File.Exists(dllPath) ? Assembly.LoadFrom(dllPath) : null;
        }
#endif
    }
}
