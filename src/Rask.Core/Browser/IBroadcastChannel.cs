using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Broadcast Channel API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Broadcast_Channel_API" />) — send
///     simple string messages between browsing contexts of the same origin (other tabs/windows of the same
///     app, and other connections on the same page). Useful for cross-tab sync: broadcast a sign-out, a
///     theme change, or a "data updated, refetch" nudge. Works on <b>both transports</b>; inject it through
///     a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Open a connection from a lifecycle hook and dispose it on unmount. A connection does
///         <em>not</em> receive its own posts — only messages from <em>other</em> connections of the same
///         name. The handler is pushed from JS, so a component that updates state in it should call
///         <c>StateHasChanged()</c> (the same pattern as subscribing to a background feed) — that's a
///         lifecycle subscription, not a render/binding callback, so RASK026 doesn't apply.
///     </para>
///     <code>
///     public sealed class Tabs(IBroadcastChannel bus) : Component, IAsyncDisposable
///     {
///         private IBroadcastChannelConnection? _conn;
///         protected override async Task OnRenderedAsync(bool first)
///         {
///             if (!first) return;
///             _conn = await bus.OpenAsync("app", msg => { /* update state */ StateHasChanged(); return Task.CompletedTask; });
///         }
///         public async ValueTask DisposeAsync() { if (_conn is not null) await _conn.DisposeAsync(); }
///     }
///     </code>
/// </remarks>
public interface IBroadcastChannel
{
    /// <summary>
    ///     Opens a connection to the channel <paramref name="name" /> and invokes
    ///     <paramref name="onMessage" /> for each message posted by another connection of the same name.
    ///     Dispose the returned connection to close it (and stop receiving).
    /// </summary>
    ValueTask<IBroadcastChannelConnection> OpenAsync(string name, Func<string, Task> onMessage);
}

/// <summary>An open broadcast-channel connection. Dispose to close it.</summary>
public interface IBroadcastChannelConnection : IAsyncDisposable
{
    /// <summary>
    ///     Posts <paramref name="message" /> to all <em>other</em> connections of this channel's name
    ///     (<c>BroadcastChannel.postMessage</c>). This connection does not receive its own message.
    /// </summary>
    ValueTask PostAsync(string message);
}

/// <summary>
///     Infrastructure for <see cref="IBroadcastChannel" /> — routes a pushed JS <c>onmessage</c> back to
///     the right C# handler by connection id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskBroadcast</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BroadcastInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<string, Task>> Handlers = new();

    internal static int Register(Func<string, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a broadcast message arrives; do not call.</summary>
    [JSInvokable("RaskBroadcastReceive")]
    public static Task Receive(int id, string message) =>
        Handlers.TryGetValue(id, out var handler) ? handler(message) : Task.CompletedTask;
}

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
