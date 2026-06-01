using ClashDetector;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashSettingsTests
    {
        [Fact]
        public void New_settings_start_with_no_models_on_either_side()
        {
            var settings = new ClashSettings();
            Assert.Empty(settings.RevitModels1);
            Assert.Empty(settings.RevitModels2);
        }
    }
}
