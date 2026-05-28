using System.Threading.Tasks;
using ClashDetector;
using ClashDetector.DevHost;
using Xunit;

namespace ClashDetector.Tests
{
    public class MockClashServiceTests
    {
        [Fact]
        public async Task RunClashesAsync_returns_two_sample_clashes()
        {
            var service = new MockClashService();
            var clashes = await service.RunClashesAsync();
            Assert.Equal(2, clashes.Count);
        }

        [Fact]
        public void Settings_exposes_seeded_models()
        {
            var service = new MockClashService();
            Assert.Equal(2, service.Settings.RevitModels1.Count);
        }
    }
}
