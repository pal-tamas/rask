using System.Text.Json;
using Rask.Core.Diagnostics;

namespace Rask.Native;

public static partial class NativeCapabilities
{
    /// <summary>
    ///     Handle a <c>{ type:"capability" }</c> envelope and answer it.
    /// </summary>
    /// <param name="messageJson">The raw message the WebView posted.</param>
    /// <param name="services">The app's services — where the native backends live.</param>
    /// <param name="evaluate">
    ///     Evaluates JavaScript in the WebView that sent the message. Passed in rather than taken from an
    ///     <c>INativeWebView</c> because the remote heads have no session and no such object; they own a
    ///     <c>WKWebView</c> / <c>android.webkit.WebView</c> directly, and this has to serve both.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the message was a capability envelope and is now dealt with;
    ///     <see langword="false" /> when it is something else and the head should handle it.
    /// </returns>
    /// <remarks>
    ///     Every outcome answers. An unknown capability, a missing backend and a backend that threw all send
    ///     a reply carrying the reason — because the page is <c>await</c>ing, and the one thing worse than an
    ///     error is a promise that never settles. That is what the previous fire-and-forget envelope did to
    ///     anything other than <c>share</c>.
    /// </remarks>
    public static async Task<bool> TryHandleAsync(
        ReadOnlyMemory<byte> messageJson, IServiceProvider services, Func<string, ValueTask> evaluate)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(evaluate);

        NativeCapabilityRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(
                messageJson.Span, NativeCapabilityJsonContext.Default.NativeCapabilityRequest);
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed capability message", ex);
            return false;
        }

        if (request is not { Type: "capability" })
        {
            return false;
        }

        var id = request.Id;
        try
        {
            var result = await NativeCapabilityDispatch
                .InvokeAsync(
                    services, request.Component ?? string.Empty, request.Op ?? string.Empty, request.Data, evaluate)
                .ConfigureAwait(false);

            await ReplyAsync(evaluate, id, true, result, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Reported as well as replied: the page learns its call failed, and the device log says why —
            // a native backend throwing on a device is not something a browser console will ever show.
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                $"[Rask.Native] capability '{request.Component}.{request.Op}' threw", ex);
            await ReplyAsync(evaluate, id, false, null, ex.Message).ConfigureAwait(false);
        }

        return true;
    }

    private static async ValueTask ReplyAsync(
        Func<string, ValueTask> evaluate, string? id, bool success, string? result, string? error)
    {
        // No id means the page did not keep a promise for this call, so there is nothing to resolve.
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new NativeCapabilityReply(id, success, result, error),
            NativeCapabilityJsonContext.Default.NativeCapabilityReply);

        // Same shape as NativeJSRuntime.EndInvokeDotNet: encode the JSON as a string literal and hand it to
        // the client, so nothing depends on reflection-based serialization at the boundary.
        await evaluate("window.__raskNative.capabilityResult(\"" + JsonEncodedText.Encode(payload) + "\")")
            .ConfigureAwait(false);
    }
}
