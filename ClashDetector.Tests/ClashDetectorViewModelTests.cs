using System.Threading.Tasks;
using ClashDetector.DevHost;
using ClashDetector.ViewModels;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashDetectorViewModelTests
    {
        private static ClashDetectorViewModel CreateViewModel()
        {
            return new ClashDetectorViewModel(new MockClashService());
        }

        [Fact]
        public void Constructor_starts_on_model_selection_view()
        {
            var vm = CreateViewModel();
            Assert.IsType<HostLinkViewModel>(vm.SelectedViewModel);
        }

        [Fact]
        public async Task RunClashesAsync_sets_status_with_clash_count()
        {
            var vm = CreateViewModel();
            await vm.RunClashesAsync();
            Assert.Equal("6 clashes found", vm.StatusMessage);
        }

        [Fact]
        public async Task RunClashesAsync_warns_when_no_models_selected_on_a_side()
        {
            var service = new MockClashService();
            foreach (var m in service.Settings.RevitModels2)
            {
                m.IsSelected = false;
            }
            var vm = new ClashDetectorViewModel(service);

            await vm.RunClashesAsync();

            Assert.Contains("Select at least one model", vm.StatusMessage);
        }
    }
}
