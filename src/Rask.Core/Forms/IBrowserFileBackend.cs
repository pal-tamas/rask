using System.Text.Json;

namespace Rask.Core.Forms;

public interface IBrowserFileBackend
{
    RaskFile Create(JsonElement metadata);

    void Release(IEnumerable<RaskFile> files);
}
