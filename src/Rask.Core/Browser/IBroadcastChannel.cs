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
///         protected override async Task OnFirstRendered()
///         {
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
