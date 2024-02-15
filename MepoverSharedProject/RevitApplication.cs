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
            //string tabname = "dontneedit";
            //application.CreateRibbonTab(tabname);
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("SheetCopier");

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData CCData = new PushButtonData("SC",
                "SheetCopier",
                thisAssemblyPath,
                "MepoverSharedProject.SheetCopier.RevitCommand");

            //MethodBase.GetCurrentMethod().DeclaringType?.FullName

            PushButton CCbutton = ribbonPanel.AddItem(CCData) as PushButton;
            CCbutton.ToolTip = "Start SheetCopier";
            //Icon = PngImageSource("MepoverSharedProject.resources.fl_icon.png");
            var assembly = Assembly.GetExecutingAssembly();
            Icon = Utils.LoadEmbeddedImage(assembly, "fl_icon.png");
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

        private System.Windows.Media.ImageSource PngImageSource(string embeddedPath)
        {
            Stream stream = GetType().Assembly.GetManifestResourceStream(embeddedPath);
            //var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);

            return decoder.Frames[0];
        }


    }
}
