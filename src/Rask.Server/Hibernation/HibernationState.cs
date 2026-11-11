namespace Rask.Server.Hibernation;

/// <summary>
///     Where one session stands on its way to hibernating: since when its tab is out of sight, when it was last asked,
///     and whether its tab said yes.
/// </summary>
/// <remarks>
///     Written by the connection's reader (the tab's <c>visibility</c> and <c>hibernate</c> frames) and read by the
///     host's round of asking on another thread, so every field is read and written through a fence. Timestamps are the
///     store's clock; zero means "not": in sight, not asked.
/// </remarks>
internal sealed class HibernationState
{
    private long _hiddenSince;
    private long _askedAt;
    private int _offered;
    private int _agreed;
    private int _trigger;

    /// <summary>When the tab went out of sight, on the store's clock; zero while it is in sight.</summary>
    public long HiddenSince => Volatile.Read(ref _hiddenSince);

    /// <summary>Whether the tab last said it is out of sight.</summary>
    public bool Hidden => HiddenSince != 0;

    /// <summary>When the host last asked, or tried to; zero when it has not since the tab was last in sight.</summary>
    public long AskedAt => Volatile.Read(ref _askedAt);

    /// <summary>Whether an offer went to the tab and has not been answered.</summary>
    public bool Offered => Volatile.Read(ref _offered) == 1;

    /// <summary>Whether the tab said yes: the session goes the moment its connection does.</summary>
    public bool Agreed => Volatile.Read(ref _agreed) == 1;

    /// <summary>What asked, for the metric.</summary>
    public HibernationTrigger Trigger => (HibernationTrigger)Volatile.Read(ref _trigger);

    /// <summary>The tab said it went out of sight (<paramref name="hidden" />) or came back, at <paramref name="now" />.</summary>
    /// <remarks>Out of sight twice keeps the first moment: a reconnect in a hidden tab says so again.</remarks>
    public void Seen(bool hidden, long now)
    {
        if (hidden)
        {
            Interlocked.CompareExchange(ref _hiddenSince, Math.Max(now, 1), 0);
            return;
        }

        Volatile.Write(ref _hiddenSince, 0);
        Volatile.Write(ref _askedAt, 0);
        Volatile.Write(ref _offered, 0);
    }

    /// <summary>The host tried at <paramref name="now" /> and the session was not ready: it is asked again later.</summary>
    public void PutOff(long now) => Volatile.Write(ref _askedAt, Math.Max(now, 1));

    /// <summary>The host is asking the tab now, for <paramref name="trigger" />.</summary>
    public void Offer(long now, HibernationTrigger trigger)
    {
        Volatile.Write(ref _trigger, (int)trigger);
        Volatile.Write(ref _askedAt, Math.Max(now, 1));
        Volatile.Write(ref _offered, 1);
    }

    /// <summary>The tab said yes. False when nothing was offered: a yes nobody asked for is not taken.</summary>
    public bool Agree()
    {
        if (Interlocked.Exchange(ref _offered, 0) != 1)
        {
            return false;
        }

        Volatile.Write(ref _agreed, 1);
        return true;
    }

    /// <summary>The tab said not now, or the session was not ready when it said yes: it is asked again later.</summary>
    public void Declined() => Volatile.Write(ref _offered, 0);

    /// <summary>A connection attached: whatever was asked of the last one is over, and this one says where the tab is.</summary>
    public void Attached()
    {
        Volatile.Write(ref _hiddenSince, 0);
        Volatile.Write(ref _askedAt, 0);
        Volatile.Write(ref _offered, 0);
        Volatile.Write(ref _agreed, 0);
    }
}
