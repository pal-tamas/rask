using System.Text.Json;

namespace Rask.Server.Hibernation;

/// <summary>
///     Lets a hidden tab's session go into the tab's resume record, and the tab rebuilds it from that record when it is
///     looked at again — Rask's answer to Blazor's circuit pause. The memory is the server's; the page is the tab's.
/// </summary>
/// <remarks>
///     <para>
///         <b>Who decides.</b> The host offers, the tab agrees. A round looks for sessions whose tab has been out of
///         sight for <see cref="SessionHibernationOptions.HiddenFor" /> — sooner, longest-hidden first, while the host is
///         short of memory or of <c>MaxSessions</c> slots — and sends each a <c>hibernate</c> frame carrying a record
///         sealed that moment. The tab says yes only while it is still hidden and holds nothing the record cannot carry:
///         input it has not sent, a form it has not saved, a video playing, an interop call under way, an event not
///         answered. The server says yes only while nothing runs in the session. Then the connection closes, the
///         session is disposed, and the tab waits, unconnected and with nothing on screen changed, until it is shown.
///     </para>
///     <para>
///         <b>What comes back</b> is exactly what a restart brings back: the address and what the app declared through
///         <c>IPersistentState</c>. The page is mounted again, so its subscriptions, timers and loads start again; what
///         it held only in fields does not survive, which is why a tab with an edit in hand never agrees.
///     </para>
///     <para>
///         <b>Nothing new to trust.</b> The record is the one every render already hands the tab: sealed under its own
///         data-protection purpose, bound to the user it was issued to, refused once
///         <c>ResumeTokenLifetime</c> has passed. A yes nobody asked for is not taken.
///     </para>
/// </remarks>
internal sealed class SessionHibernation(
    LiveSessionStore store,
    SessionHibernationOptions options,
    SessionResumeSupport resume,
    Func<MemoryLoad>? memory = null)
{
    // The reasons a tab gives for not hibernating, as the metric tags them. A tab's own words are never a tag.
    private static readonly string[] Reasons = ["shown", "busy", "input", "media", "interop"];

    private readonly Func<MemoryLoad> _memory = memory ?? MemoryLoad.Now;

    /// <summary>Whether this host can hibernate at all: only where it seals resume records.</summary>
    public bool Enabled => resume.Enabled;

    /// <summary>The values this host runs with.</summary>
    public SessionHibernationOptions Options => options;

    /// <summary>One round: reads the pressure, and asks every session that is due.</summary>
    /// <returns>How many tabs were asked.</returns>
    public async Task<int> AskDueAsync(CancellationToken ct)
    {
        if (!Enabled || store.IsDraining)
        {
            return 0;
        }

        var pressure = HibernationPressure.Read(_memory(), store.LiveCount, store.MaxSessions, options);
        var now = store.Clock.GetTimestamp();
        var asked = 0;
        foreach (var (session, trigger) in Due(store.Snapshot(), now, pressure))
        {
            ct.ThrowIfCancellationRequested();
            if (await AskAsync(session, now, trigger).ConfigureAwait(false))
            {
                asked++;
            }
        }

        return asked;
    }

    /// <summary>
    ///     The sessions to ask now, and why: every one hidden past <see cref="SessionHibernationOptions.HiddenFor" />,
    ///     and under pressure as many more as it takes, longest-hidden first.
    /// </summary>
    internal List<(LiveSession Session, HibernationTrigger Trigger)> Due(
        IEnumerable<LiveSession> sessions, long now, HibernationPressure pressure)
    {
        var due = new List<(LiveSession, HibernationTrigger)>();
        var pressed = new List<LiveSession>();
        foreach (var session in sessions)
        {
            var state = session.Hibernation;
            if (!state.Hidden || state.Agreed || !session.HasOpenTransport || AskedLately(state, now))
            {
                continue;
            }

            var hidden = store.Clock.GetElapsedTime(state.HiddenSince, now);
            if (hidden >= options.HiddenFor)
            {
                due.Add((session, HibernationTrigger.Hidden));
            }
            else if (pressure.Any && hidden >= options.HiddenUnderPressureFor)
            {
                pressed.Add(session);
            }
        }

        if (pressed.Count == 0)
        {
            return due;
        }

        // The ones out of sight the longest are the least likely to be looked at next.
        pressed.Sort(static (a, b) => a.Hibernation.HiddenSince.CompareTo(b.Hibernation.HiddenSince));
        var wanted = Math.Max(
            pressure.OverSessions - due.Count,
            pressure.Memory ? Math.Max(1, (pressed.Count + 3) / 4) : 0);
        foreach (var session in pressed.Take(wanted))
        {
            due.Add((session, HibernationTrigger.Pressure));
        }

        return due;
    }

    private bool AskedLately(HibernationState state, long now) =>
        state.AskedAt != 0 && store.Clock.GetElapsedTime(state.AskedAt, now) < options.AskAgainAfter;

    // One offer. A session with work under way is put off and asked again later; it is not the tab's to refuse.
    private async Task<bool> AskAsync(LiveSession session, long now, HibernationTrigger trigger)
    {
        try
        {
            if (await session.OfferHibernationAsync(now, trigger).ConfigureAwait(false))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The connection or the session went while it was being asked: nothing is left to hibernate.
        }

        session.Hibernation.PutOff(now);
        store.Metrics?.HibernationDeferred("server");
        return false;
    }

    /// <summary>
    ///     The tab's answer to an offer: <c>{"type":"hibernate","ok":true}</c>, or <c>"ok":false</c> with the reason.
    ///     A yes closes the connection, and the session goes with it; anything else leaves it to be asked again.
    /// </summary>
    internal static void Answered(LiveSession session, JsonElement frame, LiveSessionStore store)
    {
        if (!frame.TryGetProperty("ok"u8, out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            session.Hibernation.Declined();
            store.Metrics?.HibernationDeferred(ReasonOf(frame));
            return;
        }

        // Something started between the offer and the yes — an event the tab sent before it — or nothing was asked.
        if (!session.Settled || !session.Hibernation.Agree())
        {
            session.Hibernation.Declined();
            store.Metrics?.HibernationDeferred("server");
            return;
        }

        _ = CloseAsync(session);
    }

    // The reader goes on reading, so the close is not awaited on its thread: it sees the tab's answer to the close.
    private static async Task CloseAsync(LiveSession session)
    {
        try
        {
            await session.CloseForHibernationAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException
                                       or InvalidOperationException or System.Net.WebSockets.WebSocketException)
        {
            // The connection went first, or is already closing: the end this close was for.
        }
    }

    private static string ReasonOf(JsonElement frame)
    {
        return frame.TryGetProperty("why"u8, out var why) && why.ValueKind == JsonValueKind.String
            ? Array.Find(Reasons, why.ValueEquals) ?? "busy"
            : "busy";
    }
}
