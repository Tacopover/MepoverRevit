using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media.Imaging;
using Utilities;

namespace MepoverSharedProject
{
    public class RevitApplication : IExternalApplication
    {
        public static System.Windows.Media.ImageSource Icon;
        void AddRibbonPanel(UIControlledApplication application)
        {
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("MEPover");

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData CCData = new PushButtonData("SC",
                "SheetCopier",
                thisAssemblyPath,
                "SheetCopier.RevitCommand");

            //MethodBase.GetCurrentMethod().DeclaringType?.FullName

            PushButton CCbutton = ribbonPanel.AddItem(CCData) as PushButton;
            CCbutton.ToolTip = "Start SheetCopier";
            var assembly = Assembly.GetExecutingAssembly();
            Icon = Utils.LoadEmbeddedImage(assembly, "SheetCopier.png");
            CCbutton.LargeImage = Icon;

            // IFC Export button
            PushButtonData ifcData = new PushButtonData(
                "IFCExport",
                "IFC\nExport",
                thisAssemblyPath,
                "IfcExport.IfcExportCommand");
            PushButton ifcButton = ribbonPanel.AddItem(ifcData) as PushButton;
            ifcButton.ToolTip = "Start incremental IFC export during idle time";

        }
        public Result OnStartup(UIControlledApplication application)
        {
#if REVIT2025
            // .NET 8 does not automatically search the plugin directory for dependent assemblies.
            // Register a resolver so Xbim and other NuGet deps are found next to the plugin DLL.
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
#endif
            try
            {
                AddRibbonPanel(application);
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
