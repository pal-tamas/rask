using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Wasm.Files;

internal sealed class WasmFileBackend : IBrowserFileBackend
{
    public RaskFile Create(JsonElement metadata)
    {
        var @ref = metadata.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(@ref))
        {
            throw new InvalidOperationException("WASM file metadata is missing 'ref'.");
        }

        var name = metadata.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? string.Empty
            : string.Empty;
        var size = metadata.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number
            ? sz.GetInt64()
            : 0L;
        var contentType = metadata.TryGetProperty("type", out var ct) && ct.ValueKind == JsonValueKind.String
            ? ct.GetString() ?? "application/octet-stream"
            : "application/octet-stream";
        var lastModified = metadata.TryGetProperty("lastModified", out var lm) && lm.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(lm.GetInt64())
            : DateTimeOffset.UnixEpoch;

        return new WasmRaskFile(@ref, name, size, contentType, lastModified);
    }

    public void Release(IEnumerable<RaskFile> files)
    {
        // JS-side file registry holds File references; entries are cleared by JS on the next
        // change/submit, so no synchronous release is needed here. The dispatcher still calls
        // this for symmetry with the server backend.
    }
}
