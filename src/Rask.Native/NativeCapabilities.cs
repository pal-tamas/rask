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
public static partial class NativeCapabilities
{
    /// <summary>
    ///     The document-start script a Native + Server head injects so the loaded page can reach native
    ///     capabilities. Defines <c>window.__raskNative.capabilities</c> and <c>invoke(component, data)</c>,
    ///     which posts a <c>{ type:"capability" }</c> message over the same <c>window.__raskSend</c> /
    ///     <c>window.__raskBridge</c> channel the head already wires. Inject for your trusted origin only.
    /// </summary>
    /// <summary>
    ///     The document-start script a head injects so the loaded page can reach the native backends this
    ///     app registered. Defines <c>window.__raskNative.capabilities</c> and an <c>invoke</c> that returns
    ///     a promise, resolved by <c>capabilityResult</c> when the native side answers.
    /// </summary>
    /// <param name="capabilities">
    ///     What this head actually backs natively — see <see cref="NativeCapabilityRegistry.AdvertisedFor" />.
    ///     The page uses it to decide, per API, whether to cross the bridge or use its own JS, so a head that
    ///     backs nothing degrades to the WebView's web APIs with no branch in app code.
    /// </param>
    /// <summary>
    ///     A one-liner that tells an already-loaded client what this head backs natively. The in-process
    ///     client defines <c>window.__raskNative</c> itself and ships with an empty list; this fills it.
    /// </summary>
    public static string AdvertiseScript(IEnumerable<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var list = string.Join(",", capabilities.Select(c => "\"" + JsonEncodedText.Encode(c) + "\""));
        return "window.__raskNative.capabilities = [" + list + "];";
    }

    public static string BridgeScript(IEnumerable<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var list = string.Join(",", capabilities.Select(c => "\"" + JsonEncodedText.Encode(c) + "\""));

        return """
            (function () {
                function send(s) {
                    if (typeof window.__raskSend === "function") { window.__raskSend(s); }
                    else if (window.__raskBridge && typeof window.__raskBridge.dispatch === "function") { window.__raskBridge.dispatch(s); }
                }
                var n = window.__raskNative = window.__raskNative || {};

                // How a page that renders in the BROWSER learns it is inside a native shell. A Rask Server
                // app is told by a request header, because its document is built before any script runs; a
                // WASM app has no such moment - it boots here, in this page, after this script. So the shell
                // states it as a fact on the window, and the WASM session reads it while deciding whether to
                // draw its bars as HTML or describe them for the platform to draw.
                window.__raskShell = "native";
                n.capabilities = [__CAPS__];
                n.has = function (name) { return n.capabilities.indexOf(name) !== -1; };

                // Correlation ids and a promise table, the same shape jsResult/dotNetInvoke already use in
                // the other direction. Without this an invoke could only be fire-and-forget, which is why
                // share was the only capability that ever worked.
                var pending = {}, subs = {}, nextId = 1;
                n.invoke = function (component, op, data) {
                    var id = String(nextId++);
                    return new Promise(function (resolve, reject) {
                        pending[id] = { resolve: resolve, reject: reject };
                        send(JSON.stringify({
                            type: "capability", id: id, component: component, op: op,
                            data: data === undefined || data === null ? null : JSON.stringify(data)
                        }));
                    });
                };
                // Streams. The id is minted here, before the request is sent, so a reading that arrives
                // ahead of the reply still has a callback to reach.
                n.subscribe = function (component, op, data, onEvent) {
                    var sub = "s" + String(nextId++);
                    subs[sub] = onEvent;
                    var payload = Object.assign({ sub: sub }, data || {});
                    return n.invoke(component, op, payload).then(function () { return sub; }, function (err) {
                        delete subs[sub];
                        throw err;
                    });
                };
                n.unsubscribe = function (component, op, sub) {
                    delete subs[sub];
                    return n.invoke(component, op, sub);
                };
                n.capabilityEvent = function (json) {
                    var msg;
                    try { msg = JSON.parse(json); } catch (e) { return; }
                    var cb = subs[msg.sub];
                    if (!cb) { return; }
                    var value = null;
                    if (msg.payload !== null && msg.payload !== undefined) {
                        try { value = JSON.parse(msg.payload); } catch (e) { value = msg.payload; }
                    }
                    cb(value);
                };
                // Chrome. The session that rendered the bars is on the server, so it describes them and
                // sends the descriptor here as an ordinary JS invoke; this hands it to the head, which
                // applies it through the same INativeChrome path the in-process model has always used.
                //
                // Fire-and-forget on purpose: the page has nothing to do with the answer, and making the
                // render wait on a bar being drawn would put platform UI on the render's critical path.
                n.applyChrome = function (json) {
                    send(JSON.stringify({ type: "chrome", data: json }));
                };

                // The head echoes a bar tap back here; the page forwards it to the session that owns the
                // callback. Without this a platform bar would render and do nothing when pressed.
                n.chromeTap = function (id) {
                    if (typeof window.__raskChromeTap === "function") { window.__raskChromeTap(id); }
                };

                n.capabilityResult = function (json) {
                    var msg;
                    try { msg = JSON.parse(json); } catch (e) { return; }
                    var p = pending[msg.id];
                    if (!p) { return; }
                    delete pending[msg.id];
                    if (msg.success) {
                        var value = null;
                        if (msg.result !== null && msg.result !== undefined) {
                            try { value = JSON.parse(msg.result); } catch (e) { value = msg.result; }
                        }
                        p.resolve(value);
                    } else {
                        p.reject(new Error(msg.error || "The native capability failed."));
                    }
                };
            })();
            """.Replace("__CAPS__", list, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Whether <paramref name="url" /> is on the same <b>origin</b> (scheme + host + port) as
    ///     <paramref name="origin" /> — the trust check a Native + Server head uses to decide whether to
    ///     inject <see cref="BridgeScript" /> / keep the WebView on the page. Compares the full origin, not
    ///     just the host, so a same-host page on another port or an http downgrade is NOT trusted.
    /// </summary>
    public static bool IsTrustedOrigin(Uri origin, string? url)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return url is not null
            && Uri.TryCreate(url, UriKind.Absolute, out var u)
            && string.Equals(u.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(u.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && u.Port == origin.Port;
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
