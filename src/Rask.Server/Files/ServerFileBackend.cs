using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Server.Files;

internal sealed class ServerFileBackend : IBrowserFileBackend
{
    private readonly RaskSessionContext _session;
    private readonly SessionUploadStore _store;

    public ServerFileBackend(SessionUploadStore store, RaskSessionContext session)
    {
        _store = store;
        _session = session;
    }

    public RaskFile Create(JsonElement metadata)
    {
        var token = metadata.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("File metadata is missing 'token'.");
        }

        var entry = _store.Get(_session.Id, token)
                    ?? throw new InvalidOperationException(
                        $"Upload token '{token}' is unknown — it was never POSTed, expired, or already consumed.");
        return new ServerRaskFile(entry, _store);
    }

    public void Release(IEnumerable<RaskFile> files)
    {
        foreach (var file in files)
        {
            if (file is ServerRaskFile srf)
            {
                _store.Release(srf.SessionId, srf.Token);
            }
        }
    }
}
