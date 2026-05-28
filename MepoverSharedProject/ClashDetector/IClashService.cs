using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClashDetector
{
    public interface IClashService
    {
        ClashSettings Settings { get; }
        Task<IReadOnlyList<ClashDto>> RunClashesAsync();
    }
}
