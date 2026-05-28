namespace ClashDetector
{
    public class ClashDto
    {
        public long ElementId1 { get; set; }
        public long ElementId2 { get; set; }
        public string Document1 { get; set; }
        public string Document2 { get; set; }
        public string TypeOfClash { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rotation { get; set; }
    }
}
