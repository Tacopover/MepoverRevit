using System.Collections.Generic;
using System.Linq;
using ClashDetector;
using ClashDetector.DevHost;
using ClashDetector.ViewModels;
using Xunit;

namespace ClashDetector.Tests
{
    public class ClashResultsViewModelTests
    {
        private static ClashDto Dto(
            long id1, string name1, string cat1, string level1, bool inModel1,
            long id2, string name2, string cat2, string level2, bool inModel2,
            double overlap = 10)
        {
            return new ClashDto
            {
                ElementId1 = id1, ElementName1 = name1, Category1 = cat1, Level1 = level1, IsInOpenModel1 = inModel1, Document1 = "Host.rvt",
                ElementId2 = id2, ElementName2 = name2, Category2 = cat2, Level2 = level2, IsInOpenModel2 = inModel2, Document2 = "Link.rvt",
                OverlapVolume = overlap,
            };
        }

        private static ClashResultsViewModel CreateViewModel(params ClashDto[] dtos)
        {
            return new ClashResultsViewModel(dtos.ToList(), new MockClashService());
        }

        [Fact]
        public void Item_with_one_host_element_is_selectable()
        {
            var item = ClashResultItem.FromDto(Dto(10, "Duct", "Ducts", "Level 2", true, 20, "Wall", "Walls", "Level 2", false));
            Assert.True(item.HasSelectableElement);
            Assert.Equal(new long[] { 10 }, item.OpenModelElementIds.ToArray());
        }

        [Fact]
        public void Item_with_both_linked_is_not_selectable()
        {
            var item = ClashResultItem.FromDto(Dto(10, "Duct", "Ducts", "Level 2", false, 20, "Beam", "Framing", "Level 2", false));
            Assert.False(item.HasSelectableElement);
            Assert.Empty(item.OpenModelElementIds);
        }

        [Fact]
        public void Item_with_both_host_elements_yields_both_ids()
        {
            var item = ClashResultItem.FromDto(Dto(10, "Duct", "Ducts", "Level 2", true, 20, "Pipe", "Pipes", "Level 2", true));
            Assert.Equal(new long[] { 10, 20 }, item.OpenModelElementIds.ToArray());
        }

        [Fact]
        public void Filter_by_element1_level_narrows_the_view()
        {
            var vm = CreateViewModel(
                Dto(1, "Duct A", "Ducts", "Level 2", true, 101, "Wall", "Walls", "Level 2", false),
                Dto(2, "Pipe B", "Pipes", "Level 1", true, 102, "Floor", "Floors", "Level 1", false));

            vm.Filter1Level = "Level 2";

            var shown = vm.ClashesView.Cast<ClashResultItem>().ToList();
            Assert.Single(shown);
            Assert.Equal("Duct A", shown[0].Element1Name);
        }

        [Fact]
        public void Filter_is_case_insensitive_substring_on_element2_name()
        {
            var vm = CreateViewModel(
                Dto(1, "Duct A", "Ducts", "Level 2", true, 101, "Basic Wall", "Walls", "Level 2", false),
                Dto(2, "Pipe B", "Pipes", "Level 1", true, 102, "Floor Slab", "Floors", "Level 1", false));

            vm.Filter2Name = "wall";

            var shown = vm.ClashesView.Cast<ClashResultItem>().ToList();
            Assert.Single(shown);
            Assert.Equal("Basic Wall", shown[0].Element2Name);
        }

        [Fact]
        public void Filter_by_element1_id_narrows_the_view()
        {
            var vm = CreateViewModel(
                Dto(884211, "Duct A", "Ducts", "Level 2", true, 101, "Wall", "Walls", "Level 2", false),
                Dto(990000, "Pipe B", "Pipes", "Level 1", true, 102, "Floor", "Floors", "Level 1", false));

            vm.Filter1Id = "8842";

            var shown = vm.ClashesView.Cast<ClashResultItem>().ToList();
            Assert.Single(shown);
            Assert.Equal(884211, shown[0].ElementId1);
        }

        [Fact]
        public void SelectInRevit_is_disabled_until_a_selectable_clash_is_selected()
        {
            var vm = CreateViewModel(
                Dto(1, "Duct", "Ducts", "Level 2", true, 101, "Wall", "Walls", "Level 2", false));

            Assert.False(vm.SelectInRevitCommand.CanExecute(null));

            vm.SetSelection(new[] { vm.Clashes[0] });

            Assert.True(vm.SelectInRevitCommand.CanExecute(null));
        }

        [Fact]
        public void SelectInRevit_passes_only_host_element_ids_for_the_selection()
        {
            var service = new MockClashService();
            var vm = new ClashResultsViewModel(new List<ClashDto>
            {
                Dto(10, "Duct", "Ducts", "Level 2", true, 101, "Wall", "Walls", "Level 2", false),
                Dto(11, "ArchDuct", "Ducts", "Level 2", false, 102, "Beam", "Framing", "Level 2", false), // both linked
            }, service);

            vm.SetSelection(new[] { vm.Clashes[0], vm.Clashes[1] });
            vm.SelectInRevitCommand.Execute(null);

            Assert.Equal(new long[] { 10 }, service.LastSelectedIds.ToArray());
        }

        [Fact]
        public void Selecting_only_a_both_linked_clash_keeps_actions_disabled()
        {
            var vm = CreateViewModel(
                Dto(11, "ArchDuct", "Ducts", "Level 2", false, 102, "Beam", "Framing", "Level 2", false));

            vm.SetSelection(new[] { vm.Clashes[0] });

            Assert.False(vm.SelectInRevitCommand.CanExecute(null));
            Assert.Empty(vm.SelectableElementIds());
        }
    }
}
