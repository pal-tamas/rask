using System.Collections.Concurrent;
using System.Net;
using Rask.Hosting.Shared;

namespace Rask.Core;

/// <summary>
///     Who is reading the page, as far as the server can tell: the clock on their wall and where they connect from.
///     Inject it (<c>public MyPage(Reader reader)</c>) or ask for it (<c>Current.Service&lt;Reader&gt;()</c>).
/// </summary>
/// <remarks>
///     <para>
///         <b>The time zone</b> is the browser's own: the runtime tells the server its IANA zone
///         (<c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>) as a live session connects, and each request after
///         says it again. Until the browser has said it — the very first page a visitor opens, a request from no
///         browser — it is the app's zone (<see cref="Clock.Zone" />: <c>AddRask(timeZone: …)</c>, else the server's).
///         In a browser app it is the browser's from the start. <see cref="Now" /> reads the app's
///         <see cref="TimeProvider" />, so a test's frozen clock freezes it too.
///     </para>
///     <para>
///         <b>The address</b> is the client's as the host sees it: behind a proxy, the visitor's only when the proxy's
///         headers are trusted (<c>Rask:BehindProxy</c>, ForwardedHeaders), else the proxy's. In a live session it is the
///         address the session's connection came from, and moves when the tab reconnects from elsewhere. Null in a
///         browser app, and where the host knows none. Count a throttle by <see cref="Network" />, not by the address:
///         one IPv6 host is given a whole <c>/64</c>.
///     </para>
/// </remarks>
public sealed class Reader
{
    /// <summary>The cookie the runtime keeps the browser's IANA zone in.</summary>
    internal const string ZoneCookie = "rask-tz";

    // Zones are read from a cookie anyone can write: only a well-formed IANA id is looked up, and what is found is kept,
    // since a lookup reads the zone database from disk. The set of real zones is a few hundred.
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new(StringComparer.Ordinal);

    private readonly TimeProvider _clock;
    private volatile Seen _seen;

    internal Reader(TimeProvider? clock = null, TimeZoneInfo? zone = null, IPAddress? address = null)
    {
        _clock = clock ?? Clock.TimeProvider;
        _seen = new Seen(zone, address);
    }

    /// <summary>The reader's time zone, or the app's (<see cref="Clock.Zone" />) until their browser has said theirs.</summary>
    public TimeZoneInfo TimeZone => _seen.Zone ?? AmbientClock.Zone;

    /// <summary>Whether <see cref="TimeZone" /> is the reader's own, as their browser said it, rather than the app's.</summary>
    public bool KnowsTimeZone => _seen.Zone is not null;

    /// <summary>Now, on the reader's wall: the app's clock, in <see cref="TimeZone" /> and its offset.</summary>
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZone);

    /// <summary>Today, on the reader's calendar — the day of <see cref="Now" />.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>The address the reader connects from, or null where the host knows none (a browser app).</summary>
    public IPAddress? Address => _seen.Address;

    /// <summary>
    ///     The key to count the reader under in a throttle or a quota: their IPv4 address, or the <c>/64</c> of their
    ///     IPv6 one (an IPv4 address mapped into IPv6 counts as itself); <c>"unknown"</c> without an address.
    /// </summary>
    public string Network => NetworkOf(Address);

    /// <summary>The key <see cref="Network" /> counts <paramref name="address" /> under: IPv4 as it is, IPv6 by its <c>/64</c>.</summary>
    /// <param name="address">An address, or null.</param>
    /// <returns>The address, its <c>/64</c>, or <c>"unknown"</c>.</returns>
    public static string NetworkOf(IPAddress? address) => ClientNetwork.Of(address);

    /// <summary>Records what the host saw of the reader: the zone their browser named (an unknown id keeps the one before) and their address.</summary>
    internal void Saw(string? zoneId, IPAddress? address)
    {
        var before = _seen;
        _seen = new Seen(ZoneOf(zoneId) ?? before.Zone, address ?? before.Address);
    }

    /// <summary>The zone an IANA id names, or null when it is malformed or this machine knows no such zone.</summary>
    internal static TimeZoneInfo? ZoneOf(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !IsZoneId(id))
        {
            return null;
        }

        if (Zones.TryGetValue(id, out var known))
        {
            return known;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return null;
        }

        return Zones.GetOrAdd(id, zone);
    }

    // "Europe/Budapest", "America/Argentina/Buenos_Aires", "UTC", "Etc/GMT+5": letters, digits, '_', '+', '-', up to
    // three parts, each starting with a letter — never a dot, so never a path out of the zone database.
    private static bool IsZoneId(string id)
    {
        var parts = 0;
        var start = true;
        foreach (var c in id)
        {
            if (c == '/')
            {
                if (start || ++parts > 2)
                {
                    return false;
                }

                start = true;
                continue;
            }

            if (start ? !char.IsAsciiLetter(c) : !(char.IsAsciiLetterOrDigit(c) || c is '_' or '+' or '-'))
            {
                return false;
            }

            start = false;
        }

        return !start;
    }

    private sealed record Seen(TimeZoneInfo? Zone, IPAddress? Address);
}
