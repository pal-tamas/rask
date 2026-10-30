using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Files;

/// <summary>
///     Hands back what an event's files took out of the session's upload budget once the dispatch that carried
///     them is over — whether the handler returned, threw, timed out, read none of them, or never ran at all.
/// </summary>
/// <remarks>
///     A file is readable only inside the handler it was handed to, so from here on it is dead weight: held, it
///     counted against <see cref="RaskUploadOptions.MaxBytesPerSession" /> until the tab closed, and a tab that
///     uploaded one file after another was refused every choice past the cap.
/// </remarks>
internal static class DispatchedUploads
{
    /// <summary>
    ///     The store to hand <paramref name="frame" />'s files back to — one event, or a batch's array of them —
    ///     or null for a frame that carries none: every click and keystroke, which pays two property lookups and
    ///     no service resolution.
    /// </summary>
    public static SessionUploadStore? StoreFor(LiveSession session, JsonElement frame) =>
        !session.IsDisposed && Carries(frame) ? session.Services.GetService<SessionUploadStore>() : null;

    // An OnFiles frame names its tokens under `files`, a form submit under `form.__files` per field; a batch is an
    // array of either.
    public static void Release(SessionUploadStore store, string sessionId, JsonElement frame)
    {
        if (frame.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in frame.EnumerateArray())
            {
                Release(store, sessionId, e);
            }

            return;
        }

        if (frame.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (frame.TryGetProperty("files", out var files))
        {
            ReleaseEach(store, sessionId, files);
        }

        if (frame.TryGetProperty("form", out var form)
            && form.ValueKind == JsonValueKind.Object
            && form.TryGetProperty("__files", out var fields)
            && fields.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in fields.EnumerateObject())
            {
                ReleaseEach(store, sessionId, field.Value);
            }
        }
    }

    private static bool Carries(JsonElement frame)
    {
        if (frame.ValueKind != JsonValueKind.Array)
        {
            return CarriesOwn(frame);
        }

        // A plain loop over the struct enumerator: a batch arrives with every keystroke-then-action.
#pragma warning disable S3267 // hot path: no enumerator box or closure allocation
        foreach (var e in frame.EnumerateArray())
#pragma warning restore S3267
        {
            if (CarriesOwn(e))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CarriesOwn(JsonElement frame) =>
        frame.ValueKind == JsonValueKind.Object
        && (frame.TryGetProperty("files", out _)
            || (frame.TryGetProperty("form", out var form)
                && form.ValueKind == JsonValueKind.Object
                && form.TryGetProperty("__files", out _)));

    // Only ever this session's own staging: the store keys every entry by session, so a token from another
    // session's upload names nothing here.
    private static void ReleaseEach(SessionUploadStore store, string sessionId, JsonElement metas)
    {
        if (metas.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var meta in metas.EnumerateArray())
        {
            if (meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("token", out var token)
                && token.ValueKind == JsonValueKind.String
                && token.GetString() is { Length: > 0 } value)
            {
                store.Release(sessionId, value);
            }
        }
    }
}
