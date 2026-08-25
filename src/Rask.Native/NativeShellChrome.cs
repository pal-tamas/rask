using System.Text.Json;
using Rask.Core.Diagnostics;

namespace Rask.Native;

/// <summary>
///     The head's half of chrome in the remote models: take the descriptor a hosted session sent through
///     the bridge and apply it, and send a bar tap back the way it came.
/// </summary>
/// <remarks>
///     There is no in-process session here to hold the callbacks — the bar was declared by an app running
///     on a server, and that is where the <c>OnClick</c> lives. So the head does the two things it is
///     uniquely able to do (draw real platform chrome, notice a press) and leaves the meaning to the
///     session, which is exactly the split the in-process model already has.
/// </remarks>
internal static class NativeShellChrome
{
    /// <summary>
    ///     Handle a <c>{ type:"chrome" }</c> message from the page.
    /// </summary>
    /// <param name="messageJson">The raw message the WebView posted.</param>
    /// <param name="chrome">The head's chrome backend, or null if it draws no bars.</param>
    /// <returns>Whether the message was chrome and is now dealt with.</returns>
    public static async Task<bool> TryApplyAsync(ReadOnlyMemory<byte> messageJson, INativeChrome? chrome)
    {
        string? descriptor;
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !type.ValueEquals("chrome"u8))
            {
                return false;
            }

            descriptor = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                ? data.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed chrome message", ex);
            return false;
        }

        // Consumed either way: it WAS a chrome message, and handing it on to the session router would only
        // get it treated as an unknown event.
        if (chrome is null || string.IsNullOrEmpty(descriptor))
        {
            return true;
        }

        await chrome.ApplyChromeAsync(System.Text.Encoding.UTF8.GetBytes(descriptor)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    ///     Send a bar tap back to the page, which forwards it to the session holding the callback.
    /// </summary>
    /// <remarks>
    ///     The head raises taps as the same <c>{"type":"nativeTap","id":…}</c> shape the in-process model
    ///     uses, so this reads that shape rather than inventing a second one — one bar event, whichever
    ///     model is running.
    /// </remarks>
    public static string? TapScriptFor(ReadOnlyMemory<byte> chromeEventJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(chromeEventJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !type.ValueEquals("nativeTap"u8))
            {
                return null;
            }

            var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            return string.IsNullOrEmpty(id)
                ? null
                : "window.__raskNative.chromeTap(\"" + JsonEncodedText.Encode(id) + "\")";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
