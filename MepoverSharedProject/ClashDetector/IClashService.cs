using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClashDetector
{
    public interface IClashService
    {
        ClashSettings Settings { get; }
        Task<IReadOnlyList<ClashDto>> RunClashesAsync();

        // Selects the given host-model elements in the open document.
        void SelectInOpenModel(IEnumerable<long> elementIds);

        // Zooms/frames the open document on the given host-model elements.
        void ZoomTo(IEnumerable<long> elementIds);
    }
}
