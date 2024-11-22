using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Text;

namespace ClashDetector
{
    public class RevitClashService
    {
        public ClashSettings Settings { get; set; } = new ClashSettings();
        public UIApplication UIApp { get; private set; }
        public RevitClashService(UIApplication uiapp)
        {
            UIApp = uiapp;
        }

        public void RunClashes()
        {

        }
    }
}
