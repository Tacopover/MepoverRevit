using System.Collections.Generic;

namespace ClashDetector.ViewModels
{
    // Immutable display row for one clash in the results grid.
    public sealed class ClashResultItem
    {
        public long ElementId1 { get; private set; }
        public long ElementId2 { get; private set; }

        public string Element1Name { get; private set; }
        public string Element1Category { get; private set; }
        public string Element1Level { get; private set; }
        public string Element1Document { get; private set; }
        public bool IsElement1InOpenModel { get; private set; }

        public string Element2Name { get; private set; }
        public string Element2Category { get; private set; }
        public string Element2Level { get; private set; }
        public string Element2Document { get; private set; }
        public bool IsElement2InOpenModel { get; private set; }

        public double OverlapVolume { get; private set; }

        // At least one element lives in the open host model, so it can be selected.
        public bool HasSelectableElement
        {
            get { return IsElement1InOpenModel || IsElement2InOpenModel; }
        }

        // Element ids of this clash that live in the open host model.
        public IEnumerable<long> OpenModelElementIds
        {
            get
            {
                if (IsElement1InOpenModel)
                {
                    yield return ElementId1;
                }
                if (IsElement2InOpenModel)
                {
                    yield return ElementId2;
                }
            }
        }

        public static ClashResultItem FromDto(ClashDto dto)
        {
            return new ClashResultItem
            {
                ElementId1 = dto.ElementId1,
                ElementId2 = dto.ElementId2,
                Element1Name = dto.ElementName1,
                Element1Category = dto.Category1,
                Element1Level = dto.Level1,
                Element1Document = dto.Document1,
                IsElement1InOpenModel = dto.IsInOpenModel1,
                Element2Name = dto.ElementName2,
                Element2Category = dto.Category2,
                Element2Level = dto.Level2,
                Element2Document = dto.Document2,
                IsElement2InOpenModel = dto.IsInOpenModel2,
                OverlapVolume = dto.OverlapVolume,
            };
        }
    }
}
