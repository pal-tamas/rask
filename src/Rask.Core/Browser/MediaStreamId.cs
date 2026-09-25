namespace Rask.Core.Browser;

/// <summary>
///     Identifies one live <c>MediaStream</c> held in the browser. A <c>MediaStream</c> can't cross interop,
///     so the framework keeps it under this id and C# passes the id around instead — to attach it to a
///     <c>&lt;video&gt;</c>, to stop it, or to send it to a WebRTC peer.
/// </summary>
/// <remarks>
///     You get one from <c>IMediaDevices</c> (WASM), from <see cref="Rask.Core.Components.MediaCaptureTrigger" />
///     (Server and WASM), or from a peer's remote stream via <c>RtcHandlers.OnTrack</c>. It lives in
///     <c>Rask.Core</c> rather than beside <c>IMediaDevices</c> so every host — and
///     <see cref="IWebRtc" /> — can name one without depending on the WASM-only capture service.
/// </remarks>
/// <param name="Value">The browser-side id. Opaque; only meaningful to the framework's JS helpers.</param>
public readonly record struct MediaStreamId(int Value);
