using System.Collections.Generic;
using System.Threading.Tasks;
using ClashDetector;
using ClashDetector.ViewModels;

namespace ClashDetector.DevHost
{
    public class MockClashService : IClashService
    {
        public ClashSettings Settings { get; }

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
                new ClashDto { ElementId1 = 1001, ElementId2 = 2001, Document1 = "Host.rvt", Document2 = "Structure.rvt", TypeOfClash = "Pipes", X = 1, Y = 2, Z = 3 },
                new ClashDto { ElementId1 = 1002, ElementId2 = 2002, Document1 = "Host.rvt", Document2 = "Structure.rvt", TypeOfClash = "Ducts", X = 4, Y = 5, Z = 6 },
            };
            return Task.FromResult(sample);
        }
    }
}
