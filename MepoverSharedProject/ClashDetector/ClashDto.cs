namespace ClashDetector
{
    public class ClashDto
    {
        public long ElementId1 { get; set; }
        public long ElementId2 { get; set; }
        public string Document1 { get; set; }
        public string Document2 { get; set; }

        public string ElementName1 { get; set; }
        public string ElementName2 { get; set; }
        public string Category1 { get; set; }
        public string Category2 { get; set; }
        public string Level1 { get; set; }
        public string Level2 { get; set; }

        // True when the element lives in the currently open host document (selectable in Revit).
        public bool IsInOpenModel1 { get; set; }
        public bool IsInOpenModel2 { get; set; }

        public string TypeOfClash { get; set; }

        // Intersection volume in cm³.
        public double OverlapVolume { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rotation { get; set; }
    }
}
