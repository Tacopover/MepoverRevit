using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media.Imaging;

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
                "MepoverSharedProject.SheetCopier.RevitCommand");

            //MethodBase.GetCurrentMethod().DeclaringType?.FullName

            PushButton CCbutton = ribbonPanel.AddItem(CCData) as PushButton;
            CCbutton.ToolTip = "Start SheetCopier";
            var assembly = Assembly.GetExecutingAssembly();
            Icon = Utils.LoadEmbeddedImage(assembly, "SheetCopier.png");
            CCbutton.LargeImage = Icon;

        }
        public Result OnStartup(UIControlledApplication application)
        {
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

    }
}
