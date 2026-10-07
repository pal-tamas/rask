namespace Rask.Core.Browser;

/// <summary>
///     Connects to the WebRTC signaling relay two peers need before they can reach each other. Works on
///     <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IWebRtc" /> deliberately doesn't pick a signaling channel — an app that already has one
///         should use it. This is the channel for apps that don't, and it pairs with the server-side relay
///         (<c>AddRaskSignaling</c> / <c>MapRaskSignaling</c> in <c>Rask.Server</c>). The payload is an opaque
///         string end to end: serialize an <see cref="RtcDescription" /> or an <see cref="RtcIceCandidate" />
///         into it however you like — nothing between the two browsers looks inside.
///     </para>
///     <para>
///         The socket is separate from the live render socket, and lives in the browser on both hosts, so a
///         Server-hosted app doesn't put its own server in the middle of a relay it is already running.
///     </para>
/// </remarks>
public interface ISignaling
{
    /// <summary>Whether the browser can open a WebSocket at all.</summary>
    ValueTask<bool> IsSupported();

    /// <summary>
    ///     Connects to the relay and joins <paramref name="room" />. Dispose the result to leave — the other
    ///     peers are told.
    /// </summary>
    /// <param name="room">The room id. Opaque to the framework; the server decides who may join one.</param>
    /// <param name="handlers">The callbacks the relay pushes into.</param>
    /// <param name="path">The relay's path. Must match the server's <c>SignalingOptions.Path</c>.</param>
    ValueTask<ISignalingConnection> Join(
        string room, SignalingHandlers handlers, string path = "/rask/signaling");
}
