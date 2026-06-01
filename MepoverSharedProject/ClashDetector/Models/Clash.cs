using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Text;

namespace ClashDetector.Models
{
    public class Clash
    {
        public Document Document1 { get; set; }
        public Document Document2 { get; set; }
        public Element Element1 { get; set; }
        public Element Element2 { get; set; }
        public XYZ RevitPoint { get; set; }
        public double Rotation { get; set; }
        public string TypeOfClash;

        // Intersection volume in Revit internal units (ft³).
        public double OverlapVolume { get; set; }


        public Clash(Document document1, Document document2, Element element1, Element element2, XYZ location_ft, double rotation = 0)
        {
            Document1 = document1;
            Document2 = document2;
            Element1 = element1;
            Element2 = element2;
            Rotation = rotation;
            RevitPoint = location_ft;

        }

    }
}
