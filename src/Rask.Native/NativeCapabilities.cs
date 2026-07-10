using System.Text.Json;
using Rask.Client.Browser;
using Rask.Core.Browser;
using Rask.Core.Diagnostics;

namespace Rask.Native;

/// <summary>
///     The native device-capability bridge — how a page reaches native device backends (currently the OS
///     share sheet) through the WebView. It's the toolkit a <b>Native + Server</b> head uses to give a plain
///     remote Rask Server app device superpowers:
///     <list type="number">
///         <item>
///             inject <see cref="BridgeScript" /> at document-start (<b>only for your trusted origin</b>) so
///             the page's client sees <c>window.__raskNative.capabilities</c> + <c>invoke(name, data)</c>;
///         </item>
///         <item>
///             point the WebView's script-message handler at
///             <see cref="TryHandleAsync" />, handing it a native <see cref="IShare" /> implementation.
///         </item>
///     </list>
///     The same envelope drives the <b>Native + Local</b> path, where the in-process client
///     (<c>rask.native.js</c>) supplies the bridge and <c>NativeAppHost</c> calls <see cref="TryHandleAsync" />
///     with the DI-registered <see cref="IShare" />. The declarative <c>Shareable</c> component fires it on
///     every host; here it upgrades to the native sheet.
/// </summary>
/// <remarks>
///     <b>Security.</b> Injecting <see cref="BridgeScript" /> exposes native capabilities to <em>any</em> JS
///     on that page — inject it only for navigations to your own trusted origin(s), never for arbitrary
///     external pages. The bridge is a fixed component envelope (<c>share</c>, …), not an open native-RPC
///     channel.
/// </remarks>
public static class NativeCapabilities
{
    /// <summary>
    ///     The document-start script a Native + Server head injects so the loaded page can reach native
    ///     capabilities. Defines <c>window.__raskNative.capabilities</c> and <c>invoke(component, data)</c>,
    ///     which posts a <c>{ type:"capability" }</c> message over the same <c>window.__raskSend</c> /
    ///     <c>window.__raskBridge</c> channel the head already wires. Inject for your trusted origin only.
    /// </summary>
    public static string BridgeScript { get; } =
        """
        (function () {
            function send(s) {
                if (typeof window.__raskSend === "function") { window.__raskSend(s); }
                else if (window.__raskBridge && typeof window.__raskBridge.dispatch === "function") { window.__raskBridge.dispatch(s); }
            }
            var n = window.__raskNative = window.__raskNative || {};
            n.capabilities = ["share"];
            n.invoke = function (component, data) {
                send(JSON.stringify({ type: "capability", component: component, data: data }));
            };
        })();
        """;

    /// <summary>
    ///     Handle a WebView → .NET message posted by <see cref="BridgeScript" />'s <c>invoke</c>. If it's a
    ///     <c>{ type:"capability" }</c> envelope it's consumed (share is routed to <paramref name="share" />)
    ///     and this returns <c>true</c>; a non-capability message returns <c>false</c> so the head can handle
    ///     it otherwise. An unknown component is consumed as a no-op (forward-compatible), and a malformed
    ///     payload is discarded without throwing.
    /// </summary>
    public static async Task<bool> TryHandleAsync(ReadOnlyMemory<byte> messageJson, IShare share)
    {
        ArgumentNullException.ThrowIfNull(share);

        string? component;
        string? dataJson;
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            if (Str(root, "type") != "capability")
            {
                return false;
            }

            component = Str(root, "component");
            dataJson = Str(root, "data");
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed capability message", ex);
            return false;
        }

        if (component == "share" && !string.IsNullOrEmpty(dataJson))
        {
            await DispatchShareAsync(dataJson, share).ConfigureAwait(false);
        }

        return true;
    }

    private static async Task DispatchShareAsync(string dataJson, IShare share)
    {
        ShareData? data;
        try
        {
            data = JsonSerializer.Deserialize(dataJson, RaskBrowserJsonContext.Default.ShareData);
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed share capability payload", ex);
            return;
        }

        if (data is null)
        {
            return;
        }

        try
        {
            await share.ShareAsync(data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] share capability invoke threw", ex);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
