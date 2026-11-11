using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Hibernation;
using Rask.Server.Transport;

namespace Rask.Server;

// The session's side of hibernation (SessionHibernation): whether it may go now, the offer it sends its tab, and the
// close that lets it go once the tab said yes.
internal sealed partial class LiveSession
{
    /// <summary>Where this session stands on its way to hibernating.</summary>
    internal HibernationState Hibernation { get; } = new();

    /// <summary>
    ///     Whether a page at a path may be rebuilt from a record on this host — a devtools panel may not. Set where the
    ///     session is tied to its application; null owns every path, as for a bare test store.
    /// </summary>
    internal Func<string, bool>? Resumable { get; set; }

    /// <summary>
    ///     Whether nothing of this session is under way that its record cannot carry: no handler running or queued, no
    ///     call for the browser waiting to leave, no command mid-write, no sign-in or exit owed to the next connection.
    /// </summary>
    internal bool Settled =>
        !_disposed
        && Lock.CurrentCount == 1
        && PendingHandlers == 0
        && !InHandlerScope
        && !JsInvokes.HasPending
        && CommandsSending.IsCompleted
        && PendingAuthHandoff is null
        && PendingAuthNavigation is null
        && !SuppressEventsUntilReconnect
        && !SignInEnded
        && ArrivedUser is null
        && Volatile.Read(ref _undeliveredExit) is null;

    /// <summary>
    ///     Asks the tab to hibernate: a <c>hibernate</c> frame carrying a record sealed now, so the one the tab holds is
    ///     the page as it stands and good for the record's whole lifetime from this moment.
    /// </summary>
    /// <returns>Whether the offer went; false when the session cannot be resumed, is not settled, or has no connection.</returns>
    internal async Task<bool> OfferHibernationAsync(long now, HibernationTrigger trigger)
    {
        if (Volatile.Read(ref _transport) is not { IsOpen: true })
        {
            return false;
        }

        await _renderLock.WaitAsync(_socketCt).ConfigureAwait(false);
        try
        {
            if (!Settled || HibernationRecord() is not { } record || Volatile.Read(ref _transport) is not { IsOpen: true })
            {
                return false;
            }

            // Before the send: an answer read on the connection's own thread must find the offer.
            Hibernation.Offer(now, trigger);
            await SendGuardedAsync(OfferFrame(record)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ConnectionWentUnder(ex))
        {
            Hibernation.Declined();
            return false;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>A connection attached: the tab is not leaving, and whatever was asked of the last one is over.</summary>
    internal void Reattached()
    {
        Leaving = false;
        Hibernation.Attached();
    }

    /// <summary>Ends the connection, as the tab agreed: its session goes when the connection does (<see cref="LiveSessionStore.Disconnected" />).</summary>
    internal Task CloseForHibernationAsync() => CloseTransportAsync(LiveTransportClose.Normal, "hibernate", _socketCt);

    // The record the tab rebuilds the page from: where it is and what the app declared. Under the render lock, which
    // every render that reads the bag holds too. The address is built here, not from RouteUrl's cache, which only the
    // render path keeps.
    private string? HibernationRecord()
    {
        if (ResumeProtector() is not { } protector)
        {
            return null;
        }

        var route = Services.GetRequiredService<RouteState>();
        if (Resumable is { } resumable && !resumable(route.Path))
        {
            return null;
        }

        var state = Services.GetRequiredService<PersistentState>();
        return Seal(protector, QueryString.Build(route.Path, route.Query), state, firstPage: false);
    }

    private static byte[] OfferFrame(string record)
    {
        var buffer = new ArrayBufferWriter<byte>(48 + record.Length);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "hibernate"u8);
            writer.WriteString("resume"u8, record);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
