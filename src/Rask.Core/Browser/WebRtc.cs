using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IWebRtc" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>RTCPeerConnection</c> and its channels stay in the browser under the framework's
///     <c>__raskRtc</c> helper, addressed by id; every push comes back through
///     <see cref="WebRtcInterop" />.
/// </summary>
public sealed class WebRtc : IWebRtc
{
    private readonly IJSRuntime _js;

    // Root WebRtcInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(WebRtcInterop))]
    public WebRtc(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskRtc.isSupported");

    /// <inheritdoc />
    public async ValueTask<IPeerConnection> CreateAsync(RtcConfiguration config, RtcHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(handlers);
        Validate(config);

        // Register before creating: ICE gathering starts as soon as a description is set, and a handler
        // registered afterwards would miss the first candidates.
        var id = WebRtcInterop.RegisterConnection(handlers, _js);
        try
        {
            await _js.InvokeVoidAsync("__raskRtc.create", id, config);
        }
        catch
        {
            WebRtcInterop.UnregisterConnection(id);
            throw;
        }

        return new PeerConnection(_js, id);
    }

    // An ICE server URL comes from app configuration and is handed to the browser verbatim. Anything that
    // isn't a STUN/TURN scheme would be a misconfiguration at best, so reject it here rather than let the
    // browser decide what to do with it.
    private static void Validate(RtcConfiguration config)
    {
        var invalid = (config.IceServers ?? []).FirstOrDefault(url =>
            !url.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase));
        if (invalid is not null)
        {
            throw new ArgumentException(
                $"ICE server URL '{invalid}' must use the stun:, turn: or turns: scheme.", nameof(config));
        }

        if (config.IceTransportPolicy is not (null or "all" or "relay"))
        {
            throw new ArgumentException(
                $"IceTransportPolicy must be \"all\" or \"relay\", not '{config.IceTransportPolicy}'.",
                nameof(config));
        }
    }

    private sealed class PeerConnection(IJSRuntime js, int id) : IPeerConnection
    {
        private bool _disposed;

        public ValueTask<RtcDescription> CreateOfferAsync() =>
            js.InvokeAsync<RtcDescription>("__raskRtc.createOffer", id);

        public ValueTask<RtcDescription> CreateAnswerAsync() =>
            js.InvokeAsync<RtcDescription>("__raskRtc.createAnswer", id);

        public ValueTask SetLocalDescriptionAsync(RtcDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);
            return js.InvokeVoidAsync("__raskRtc.setLocal", id, description);
        }

        public ValueTask SetRemoteDescriptionAsync(RtcDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);
            return js.InvokeVoidAsync("__raskRtc.setRemote", id, description);
        }

        public ValueTask AddIceCandidateAsync(RtcIceCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return js.InvokeVoidAsync("__raskRtc.addIce", id, candidate);
        }

        public ValueTask AddStreamAsync(IJSObjectReference stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return js.InvokeVoidAsync("__raskRtc.addStream", id, stream);
        }

        public ValueTask RemoveStreamAsync(IJSObjectReference stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return js.InvokeVoidAsync("__raskRtc.removeStream", id, stream);
        }

        public async ValueTask<IRtcDataChannel> CreateDataChannelAsync(
            string label, RtcDataChannelOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(label);

            var channelId = await js.InvokeAsync<int>("__raskRtc.createChannel", id, label, options);
            var channel = new DataChannel(js, id, channelId, label);
            WebRtcInterop.RegisterChannel(id, channelId, channel);
            return channel;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            WebRtcInterop.UnregisterConnection(id);
            await js.InvokeVoidAsync("__raskRtc.close", id);
        }
    }

    // Internal rather than private: WebRtcInterop builds one of these when the remote peer opens a
    // channel, using the runtime of the session that owns the connection.
    internal sealed class DataChannel(IJSRuntime js, int connectionId, int id, string label) : IRtcDataChannel
    {
        private bool _disposed;

        public string Label => label;

        public ValueTask ListenAsync(Func<IReadOnlyList<RtcMessage>, Task> onMessages)
        {
            ArgumentNullException.ThrowIfNull(onMessages);
            WebRtcInterop.Listen(connectionId, id, onMessages);
            return js.InvokeVoidAsync("__raskRtc.listen", id);
        }

        public ValueTask SendAsync(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return js.InvokeVoidAsync("__raskRtc.sendText", id, text);
        }

        public ValueTask SendAsync(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return js.InvokeVoidAsync("__raskRtc.sendBytes", id, Convert.ToBase64String(data));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            WebRtcInterop.UnregisterChannel(connectionId, id);
            await js.InvokeVoidAsync("__raskRtc.closeChannel", id);
        }
    }
}
