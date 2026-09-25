using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IBroadcastChannel" />, backed by the unified <see cref="IJSRuntime" />. Each
///     connection gets an integer id; the framework's <c>__raskBroadcast</c> helper holds the live
///     <c>BroadcastChannel</c> and calls back into <see cref="BroadcastInterop.Receive" /> (a static
///     <c>[JSInvokable]</c>, so one wiring serves both transports without marshalling a
///     <c>DotNetObjectReference</c>).
/// </summary>
public sealed class BroadcastChannelService : IBroadcastChannel
{
    private readonly IJSRuntime _js;

    // Root BroadcastInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Receive method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BroadcastInterop))]
    public BroadcastChannelService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IBroadcastChannelConnection> OpenAsync(string name, Func<string, Task> onMessage)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(onMessage);

        var id = BroadcastInterop.Register(onMessage);
        try
        {
            await _js.InvokeVoidAsync("__raskBroadcast.open", id, name);
        }
        catch
        {
            BroadcastInterop.Unregister(id);
            throw;
        }

        return new Connection(_js, id);
    }

    private sealed class Connection(IJSRuntime js, int id) : IBroadcastChannelConnection
    {
        private bool _closed;

        public ValueTask PostAsync(string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return js.InvokeVoidAsync("__raskBroadcast.post", id, message);
        }

        public async ValueTask DisposeAsync()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            BroadcastInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskBroadcast.close", id);
        }
    }
}
