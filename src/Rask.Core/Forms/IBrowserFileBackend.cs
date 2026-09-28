using System.Text.Json;

namespace Rask.Core.Forms;

public interface IBrowserFileBackend
{
    IRaskFile Create(JsonElement metadata);

    void Release(IEnumerable<IRaskFile> files);
}
