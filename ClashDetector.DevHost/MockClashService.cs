using System.Collections.Generic;
using System.Threading.Tasks;
using ClashDetector;
using ClashDetector.ViewModels;

namespace ClashDetector.DevHost
{
    public class MockClashService : IClashService
    {
        public ClashSettings Settings { get; }

        // Last ids passed to selection/zoom — handy for manual DevHost inspection.
        public IReadOnlyList<long> LastSelectedIds { get; private set; } = new List<long>();
        public IReadOnlyList<long> LastZoomedIds { get; private set; } = new List<long>();

        public MockClashService()
        {
            Settings = new ClashSettings();
            Settings.RevitModels1.Add(new ListItem("Host.rvt", true));
            Settings.RevitModels1.Add(new ListItem("Structure.rvt"));
            Settings.RevitModels2.Add(new ListItem("Host.rvt"));
            Settings.RevitModels2.Add(new ListItem("Structure.rvt", true));
        }

        public Task<IReadOnlyList<ClashDto>> RunClashesAsync()
        {
            IReadOnlyList<ClashDto> sample = new List<ClashDto>
            {
                Clash(1001, "Rectangular Duct 400x200", "Ducts", "Level 2", true, "Host.rvt",
                      2001, "Basic Wall - Interior 150", "Walls", "Level 2", false, "Structure.rvt", 128.4),
                Clash(1002, "Pipe Type - Std 110mm", "Pipes", "Level 1", true, "Host.rvt",
                      2002, "Floor - Concrete 200", "Floors", "Level 1", false, "Structure.rvt", 24.0),
                Clash(1003, "Cable Tray 300mm", "Cable Trays", "Level 3", true, "Host.rvt",
                      2003, "Structural Beam W12x26", "Structural Framing", "Level 3", false, "Structure.rvt", 342.7),
                Clash(1004, "Rectangular Duct 600x300", "Ducts", "Level 2", true, "Host.rvt",
                      2004, "Round Column 400mm", "Columns", "Level 2", false, "Structure.rvt", 9.1),
                Clash(1005, "Conduit 25mm", "Conduits", "Level 3", true, "Host.rvt",
                      2005, "Floor - Slab 250", "Floors", "Level 3", false, "Structure.rvt", 63.2),
                // both elements in linked models — NOT selectable
                Clash(3001, "Duct - Arch coord 250x150", "Ducts", "Level 2", false, "Arch.rvt",
                      2006, "Structural Beam W10x22", "Structural Framing", "Level 2", false, "Structure.rvt", 47.5),
            };
            return Task.FromResult(sample);
        }

        public void SelectInOpenModel(IEnumerable<long> elementIds)
        {
            LastSelectedIds = elementIds == null ? new List<long>() : new List<long>(elementIds);
        }

        public void ZoomTo(IEnumerable<long> elementIds)
        {
            LastZoomedIds = elementIds == null ? new List<long>() : new List<long>(elementIds);
        }

        private static ClashDto Clash(
            long id1, string name1, string cat1, string level1, bool inModel1, string doc1,
            long id2, string name2, string cat2, string level2, bool inModel2, string doc2,
            double overlap)
        {
            return new ClashDto
            {
                ElementId1 = id1, ElementName1 = name1, Category1 = cat1, Level1 = level1, IsInOpenModel1 = inModel1, Document1 = doc1,
                ElementId2 = id2, ElementName2 = name2, Category2 = cat2, Level2 = level2, IsInOpenModel2 = inModel2, Document2 = doc2,
                OverlapVolume = overlap,
            };
        }
    }
}
