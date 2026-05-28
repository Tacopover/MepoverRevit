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
        public void Constructor_starts_on_categories_view()
        {
            var vm = CreateViewModel();
            Assert.IsType<CategoriesViewModel>(vm.SelectedViewModel);
        }

        [Fact]
        public void NavigateTo_models_switches_to_host_link_view()
        {
            var vm = CreateViewModel();
            vm.NavigateToCommand.Execute("Models");
            Assert.IsType<HostLinkViewModel>(vm.SelectedViewModel);
        }

        [Fact]
        public async Task RunClashesAsync_sets_status_with_clash_count()
        {
            var vm = CreateViewModel();
            await vm.RunClashesAsync();
            Assert.Equal("2 clashes found", vm.StatusMessage);
        }
    }
}
