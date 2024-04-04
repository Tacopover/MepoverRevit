using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UIFramework;

namespace MepoverSharedProject.SheetCopier
{
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class RevitCommand : IExternalCommand
    {
        private SheetCopierViewModel mainViewModel;
        public static IntPtr WindowHandle;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                if (mainViewModel == null)
                {
                    mainViewModel = new SheetCopierViewModel(uiApp);
                }
                else
                {
                    if (mainViewModel.IsWindowClosed)
                    {

                        mainViewModel.ShowMainWindow();
                    }
                    else
                    {
                        mainViewModel.MainWindow.Activate();
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                string errormessage = ex.GetType().Name + " " + ex.StackTrace.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                MessageBox.Show(errormessage);
                return Result.Failed;
            }
        }

        public static void CreatePanelButton(RibbonPanel ribbonPanel)
        {
            string thisAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            PushButtonData CCData = new PushButtonData("SC",
                               "SheetCopier",
                                              thisAssemblyPath,
                                                             typeof(RevitCommand).FullName);
            PushButton CCbutton = ribbonPanel.AddItem(CCData) as PushButton;
            CCbutton.ToolTip = "Start SheetCopier";
            //CCbutton.LargeImage = mainViewModel.Icon;
        }
    }
}
