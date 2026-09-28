using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="ISignaling" />, backed by the unified <see cref="IJSRuntime" /> and the framework's
///     <c>__raskSignal</c> helper, which owns the WebSocket.
/// </summary>
public sealed class Signaling : ISignaling
{
    private readonly IJSRuntime _js;

    // Root SignalingInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(SignalingInterop))]
    public Signaling(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskSignal.isSupported");

    /// <inheritdoc />
    public async ValueTask<ISignalingConnection> JoinAsync(
        string room, SignalingHandlers handlers, string path = "/rask/signaling")
    {
        ArgumentException.ThrowIfNullOrEmpty(room);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Register before connecting: the relay answers a join immediately, and a handler registered after
        // the fact would miss the peer list it replies with.
        var id = SignalingInterop.Register(_js, handlers);
        try
        {
            await _js.InvokeVoidAsync("__raskSignal.open", id, path);
            await _js.InvokeVoidAsync("__raskSignal.send", id, Join(room));
        }
        catch
        {
            SignalingInterop.Unregister(id);
            throw;
        }

        return new Connection(_js, id);
    }

    private static string Join(string room) => JsonSerializer.Serialize(
        new SignalingJoin("join", room), RaskBrowserJsonContext.Default.SignalingJoin);

    private sealed class Connection(IJSRuntime js, int id) : ISignalingConnection
    {
        private bool _disposed;

        public ValueTask SendAsync(string toPeerId, string payload)
        {
            ArgumentException.ThrowIfNullOrEmpty(toPeerId);
            ArgumentNullException.ThrowIfNull(payload);

            var json = JsonSerializer.Serialize(
                new SignalingSignal("signal", toPeerId, payload),
                RaskBrowserJsonContext.Default.SignalingSignal);
            return js.InvokeVoidAsync("__raskSignal.send", id, json);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SignalingInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskSignal.close", id);
        }
    }
}
