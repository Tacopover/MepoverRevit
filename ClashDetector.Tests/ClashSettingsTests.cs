using System.Linq;
using ClashDetector;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashSettingsTests
    {
        [Fact]
        public void Default_categories_include_preselected_pipes()
        {
            var settings = new ClashSettings();
            var pipes = settings.Categories1.Single(c => c.Name == "Pipes");
            Assert.True(pipes.IsSelected);
        }
    }
}
