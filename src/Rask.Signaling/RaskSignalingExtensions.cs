using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rask.Hosting.Shared;

namespace Rask.Signaling;

/// <summary>
///     Hosts the WebRTC signaling relay two peers need before they can connect
///     (see <c>docs/apis/webrtc.md</c>). Opt-in: call <see cref="AddRaskSignaling" /> and
///     <see cref="MapRaskSignaling" />.
/// </summary>
/// <remarks>
///     <para>
///         The relay carries <b>opaque strings</b>. It never parses an SDP or an ICE candidate — the payload
///         is whatever the client put there, forwarded verbatim to one named peer in the same room. That is
///         deliberate: parsing attacker-controlled SDP server-side would add a lot of surface for no benefit,
///         since only the browsers need to understand it.
///     </para>
///     <para>
///         <b>It is a relay between untrusted peers</b>, so: peer ids are minted by the server and never
///         taken from the client; a message is only delivered to a peer in the sender's own room; nothing is
///         ever echoed back to its sender; payloads and message rates are capped; and joining requires
///         authentication by default, with <see cref="SignalingOptions.AuthorizeRoom" /> as the hook for
///         "may this user join <em>this</em> room".
///     </para>
/// </remarks>
public static class RaskSignalingExtensions
{
    /// <summary>Registers the signaling relay. Pair with <see cref="MapRaskSignaling" />.</summary>
    /// <remarks>
    ///     <see cref="SignalingOptions" /> reads the <c>Rask:Signaling</c> configuration section first, then
    ///     <paramref name="configure" />, so code wins. <see cref="SignalingOptions.AuthorizeRoom" /> is a
    ///     delegate and can only be set in code. Idempotent: the first call's options win.
    /// </remarks>
    public static IServiceCollection AddRaskSignaling(
        this IServiceCollection services, Action<SignalingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validated when the host starts — and MapRaskSignaling resolves the options while mapping, so a bad
        // value stops the app before a single peer can connect either way.
        services.AddRaskOptions("Rask:Signaling", static (section, o) => section.Bind(o), configure, Validate);
        services.TryAddSingleton<SignalingHub>();
        return services;
    }

    /// <summary>
    ///     Maps the signaling WebSocket endpoint at <see cref="SignalingOptions.Path" />. Requires
    ///     <see cref="AddRaskSignaling" />.
    /// </summary>
    public static IEndpointRouteBuilder MapRaskSignaling(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<SignalingOptions>()
                      ?? throw new InvalidOperationException(
                          "MapRaskSignaling() needs AddRaskSignaling() — the relay's options and room "
                          + "registry are resolved from DI.");

        var builder = endpoints.MapGet(options.Path, RunAsync);

        // Authorization is the default, and it is expressed on the endpoint so a host's own policies apply.
        // The opt-out is explicit rather than implied by the absence of a call.
        if (options.RequireAuthorization)
        {
            builder.RequireAuthorization();
        }
        else
        {
            builder.AllowAnonymous();
        }

        return endpoints;
    }

    // Every failure names the setting ("MaxPeersPerRoom (1) must be at least 2.") — the key someone has to go
    // and fix; the options registration prefixes the section.
    private static void Validate(SignalingOptions o)
    {
        if (!o.Path.StartsWith('/'))
        {
            throw new InvalidOperationException($"Path must start with '/', not '{o.Path}'.");
        }

        AtLeast(nameof(o.MaxMessageBytes), o.MaxMessageBytes, 1024);
        AtLeast(nameof(o.MaxPayloadBytes), o.MaxPayloadBytes, 256);
        if (o.MaxPayloadBytes > o.MaxMessageBytes)
        {
            throw new InvalidOperationException(
                $"MaxPayloadBytes ({o.MaxPayloadBytes}) must be at most MaxMessageBytes ({o.MaxMessageBytes}).");
        }

        AtLeast(nameof(o.MaxPeersPerRoom), o.MaxPeersPerRoom, 2);
        AtLeast(nameof(o.MaxRooms), o.MaxRooms, 1);
        AtLeast(nameof(o.MaxPeersPerClient), o.MaxPeersPerClient, 1);
        AtLeast(nameof(o.MaxRoomIdLength), o.MaxRoomIdLength, 1);
        AtLeast(nameof(o.MaxMessagesPerSecond), o.MaxMessagesPerSecond, 0);

        foreach (var origin in o.AllowedOrigins)
        {
            // An origin is scheme, host and port — what a browser puts in the header. A trailing path would
            // never match one, so it is refused here rather than silently admitting nobody.
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"AllowedOrigins holds '{origin}', which is not an origin. Write scheme and host, "
                    + "with a port if it is not the default and no path: https://app.example.com.");
            }
        }
    }

    // A browser always names the page behind a WebSocket upgrade, and sends the visitor's cookie with it.
    private static bool FromAnAllowedPage(HttpRequest request, SignalingOptions options) =>
        SameOrigin.Allows(request)
        || options.AllowedOrigins.Contains(request.Headers.Origin.ToString(), StringComparer.OrdinalIgnoreCase);

    private static void AtLeast(string setting, int value, int minimum)
    {
        if (value < minimum)
        {
            throw new InvalidOperationException($"{setting} ({value}) must be at least {minimum}.");
        }
    }

    private static async Task RunAsync(HttpContext ctx)
    {
        // Without UseWebSockets() there is no upgrade feature at all, and IsWebSocketRequest is false for
        // every request — so a host that forgot it would see the relay refuse perfectly good clients with
        // a bare 400 and nothing to go on. Tell it apart from an actual non-WebSocket request.
        if (ctx.Features.Get<IHttpWebSocketFeature>() is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsync(
                "The Rask signaling relay needs WebSocket support in the pipeline. Call app.UseWebSockets() "
                + "before app.MapRaskSignaling(). (Rask.Server's MapRask() does this for you; a static-file "
                + "host serving a published WASM bundle does not.)", ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var options = ctx.RequestServices.GetRequiredService<SignalingOptions>();

        // Cross-Site WebSocket Hijacking: refused before the upgrade, so another site's page never holds
        // a socket that AuthorizeRoom would then be asked about as the signed-in visitor.
        if (!FromAnAllowedPage(ctx.Request, options))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var hub = ctx.RequestServices.GetRequiredService<SignalingHub>();
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await ServeAsync(ctx, socket, hub, options).ConfigureAwait(false);
    }

    // One connection: its writer runs beside its receive loop, and both end before the socket is closed.
    private static async Task ServeAsync(HttpContext ctx, WebSocket socket, SignalingHub hub, SignalingOptions options)
    {
        using var outbox = new PeerOutbox();
        var writing = outbox.RunAsync(socket, ctx.RequestAborted);
        var joined = new StrongBox<Peer?>();
        string? closing = null;
        try
        {
            closing = await PumpAsync(ctx, socket, outbox, hub, options, joined).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client went away, or the host is shutting down. Neither is an error.
        }
        catch (WebSocketException)
        {
            // An abrupt disconnect. Also not an error — the finally below still cleans up.
        }
        finally
        {
            if (joined.Value is { } peer)
            {
                hub.Leave(peer);

                // Queued for the others, never sent from here: a peer leaves most often by going away, and the
                // rest of the room must still hear it — without waiting on whichever of them reads slowest.
                Announce(hub, peer, "peer-left");
            }

            // What was queued for this peer goes first; a close frame is a send too, and only one may run.
            outbox.Complete();
            await writing.ConfigureAwait(false);
            if (closing is not null)
            {
                await CloseAsync(socket, closing, ctx.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    // Records the joined peer in <paramref name="joined" /> the moment it joins, not on return: a peer that goes
    // away abruptly ends the loop with an exception, and a peer known only from a return value would then never
    // leave its room — still counted against its caps, and never announced to the peers it left behind. Returns
    // why the socket is to be closed, or null when the client closed it.
    private static async Task<string?> PumpAsync(
        HttpContext ctx, WebSocket socket, PeerOutbox outbox, SignalingHub hub, SignalingOptions options,
        StrongBox<Peer?> joined)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.MaxMessageBytes);
        var windowStart = Environment.TickCount64;
        var inWindow = 0;

        try
        {
            while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
            {
                var (bytes, closed) = await ReceiveAsync(socket, buffer, ctx.RequestAborted).ConfigureAwait(false);
                if (closed)
                {
                    return null;
                }

                if (bytes < 0)
                {
                    // Oversized: the message never fits, so there is nothing to resynchronise to.
                    return "message too large";
                }

                if (options.MaxMessagesPerSecond > 0)
                {
                    var now = Environment.TickCount64;
                    if (now - windowStart >= 1000)
                    {
                        windowStart = now;
                        inWindow = 0;
                    }

                    if (++inWindow > options.MaxMessagesPerSecond)
                    {
                        return "rate limit";
                    }
                }

                joined.Value = await HandleAsync(ctx, outbox, hub, options, buffer.AsMemory(0, bytes), joined.Value)
                    .ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // bytes < 0 signals "larger than the cap"; closed signals a clean or client-initiated close.
    private static async Task<(int Bytes, bool Closed)> ReceiveAsync(
        WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Complete the handshake, so a client that called CloseAsync (and is waiting for our close
                // frame) isn't left hanging until its own timeout. CloseOutputAsync rather than CloseAsync:
                // we are already the side that received the close, so there is nothing left to wait for.
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException
                                               or ObjectDisposedException)
                {
                    // Already torn down; the reply was a courtesy.
                }

                return (0, true);
            }

            offset += result.Count;
            if (result.EndOfMessage)
            {
                return (offset, false);
            }

            if (offset >= buffer.Length)
            {
                return (-1, false);
            }
        }
    }

    private static async Task<Peer?> HandleAsync(
        HttpContext ctx, PeerOutbox outbox, SignalingHub hub, SignalingOptions options,
        ReadOnlyMemory<byte> message, Peer? peer)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            Error(outbox, "malformed message");
            return peer;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
            {
                Error(outbox, "malformed message");
                return peer;
            }

            switch (typeEl.GetString())
            {
                case "join":
                    return await JoinAsync(ctx, outbox, hub, options, root, peer).ConfigureAwait(false);
                case "signal":
                    return Signal(outbox, hub, options, root, peer);
                default:
                    Error(outbox, "unknown message type");
                    return peer;
            }
        }
    }

    private static async Task<Peer?> JoinAsync(
        HttpContext ctx, PeerOutbox outbox, SignalingHub hub, SignalingOptions options,
        JsonElement root, Peer? peer)
    {
        if (peer is not null)
        {
            Error(outbox, "already joined");
            return peer;
        }

        if (!root.TryGetProperty("room", out var roomEl) || roomEl.ValueKind != JsonValueKind.String)
        {
            Error(outbox, "join needs a room");
            return null;
        }

        var room = roomEl.GetString()!;
        if (room.Length == 0 || room.Length > options.MaxRoomIdLength)
        {
            Error(outbox, "invalid room");
            return null;
        }

        var user = ctx.User ?? new System.Security.Claims.ClaimsPrincipal();
        if (!await options.AuthorizeRoom(new SignalingJoinContext(room, user, ctx.RequestServices)).ConfigureAwait(false))
        {
            // Same wording as a full room on purpose: whether a room exists, and who is in it, is not
            // something an unauthorized caller should be able to probe.
            Error(outbox, "cannot join");
            return null;
        }

        var joined = hub.Join(room, outbox, ClientOf(ctx), out var existing);
        if (joined is null)
        {
            Error(outbox, "cannot join");
            return null;
        }

        outbox.Post(Json(w =>
        {
            w.WriteString("type", "joined");
            w.WriteString("peerId", joined.Id);
            w.WriteStartArray("peers");
            foreach (var id in existing)
            {
                w.WriteStringValue(id);
            }

            w.WriteEndArray();
        }));

        Announce(hub, joined, "peer-joined");
        return joined;
    }

    // Who a peer counts against: the signed-in user, or — for anyone else — the network the socket came from.
    private static string ClientOf(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? "user:" + (ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ctx.User.Identity.Name)
            : "net:" + ClientNetwork.Of(ctx.Connection.RemoteIpAddress);

    private static Peer? Signal(
        PeerOutbox outbox, SignalingHub hub, SignalingOptions options, JsonElement root, Peer? peer)
    {
        if (peer is null)
        {
            Error(outbox, "join first");
            return null;
        }

        if (!root.TryGetProperty("to", out var toEl) || toEl.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.String)
        {
            Error(outbox, "signal needs `to` and `payload`");
            return peer;
        }

        var payload = payloadEl.GetString()!;
        if (Encoding.UTF8.GetByteCount(payload) > options.MaxPayloadBytes)
        {
            Error(outbox, "payload too large");
            return peer;
        }

        // Membership is checked here, not trusted from the message — this is what stops a peer addressing
        // someone in another room, or addressing itself to have the relay echo for it. A target that has gone,
        // or was cut off for not reading, is no peer either: queued, never awaited, so it cannot hold the sender.
        var target = hub.Target(peer, toEl.GetString()!);
        var sent = target is not null && target.Outbox.Post(Json(w =>
        {
            w.WriteString("type", "signal");
            w.WriteString("from", peer.Id);
            w.WriteString("payload", payload);
        }));
        if (!sent)
        {
            Error(outbox, "no such peer");
        }

        return peer;
    }

    // Queued for every other peer in the room. One that has gone, or is not keeping up, misses it — and is on its
    // way out of the room already.
    private static void Announce(SignalingHub hub, Peer peer, string type)
    {
        var frame = Json(w =>
        {
            w.WriteString("type", type);
            w.WriteString("peerId", peer.Id);
        });

        foreach (var other in hub.Others(peer))
        {
            other.Outbox.Post(frame);
        }
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    // Through the connection's own queue like every other frame, so it never races a relayed one onto the socket.
    private static void Error(PeerOutbox outbox, string message) =>
        outbox.Post(Json(w =>
        {
            w.WriteString("type", "error");
            w.WriteString("message", message);
        }));

    private static async Task CloseAsync(WebSocket socket, string reason, CancellationToken ct)
    {
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException
                                       or ObjectDisposedException)
        {
            // Already gone; the close frame was a courtesy.
        }
    }
}
