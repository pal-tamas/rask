using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="ISignaling" /> — routes relay messages back to the right C# callbacks
///     by connection id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskSignal</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SignalingInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, SignalingHandlers> Handlers = new();

    internal static int Register(SignalingHandlers handlers)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handlers;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each relay message; do not call.</summary>
    [JSInvokable("RaskSignalMessage")]
    public static Task Message(int id, string type, string peerId, string payload)
    {
        if (!Handlers.TryGetValue(id, out var h))
        {
            return Task.CompletedTask;
        }

        return type switch
        {
            // `payload` carries the peer list for a join — the one message where it isn't an app payload.
            "joined" => h.OnJoined is null ? Task.CompletedTask : h.OnJoined(peerId, ParsePeers(payload)),
            "peer-joined" => h.OnPeerJoined is null ? Task.CompletedTask : h.OnPeerJoined(peerId),
            "peer-left" => h.OnPeerLeft is null ? Task.CompletedTask : h.OnPeerLeft(peerId),
            "signal" => h.OnSignal is null ? Task.CompletedTask : h.OnSignal(peerId, payload),
            "error" => h.OnError is null ? Task.CompletedTask : h.OnError(payload),
            _ => Task.CompletedTask
        };
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when the socket closes; do not call.</summary>
    [JSInvokable("RaskSignalClosed")]
    public static Task Closed(int id)
    {
        if (!Handlers.TryRemove(id, out var h))
        {
            return Task.CompletedTask;
        }

        return h.OnClosed is null ? Task.CompletedTask : h.OnClosed();
    }

    private static List<string> ParsePeers(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var peers = new List<string>(document.RootElement.GetArrayLength());
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    peers.Add(element.GetString()!);
                }
            }

            return peers;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
