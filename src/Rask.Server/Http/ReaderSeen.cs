using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Server.Http;

/// <summary>
///     What a request says of its reader — the zone their browser keeps in its cookie (rask-zone.ts), and the address
///     it came from (after ForwardedHeaders, when <c>Rask:BehindProxy</c> trusts them) — and where it goes: a live
///     session's <see cref="Reader" /> as the session is opened and every time a connection attaches to it, a plain
///     request's own as it is first asked for.
/// </summary>
internal readonly record struct ReaderSeen(string? Zone, IPAddress? Address)
{
    /// <summary>What <paramref name="http" /> says of its reader.</summary>
    internal static ReaderSeen Of(HttpContext? http) =>
        http is null ? default : new ReaderSeen(http.Request.Cookies[Reader.ZoneCookie], http.Connection.RemoteIpAddress);

    /// <summary>One <see cref="Reader" /> per session (and per plain request), seeded from the request that made it.</summary>
    internal static void Register(IServiceCollection services) =>
        services.AddScoped(static sp =>
        {
            var reader = new Reader(sp.GetService<TimeProvider>());
            Of(sp.GetService<IHttpContextAccessor>()?.HttpContext).Tell(reader);
            return reader;
        });

    /// <summary>
    ///     <paramref name="attach" />, having told the session it attached what this connection's request saw: the
    ///     session is read through this connection now, so its reader's zone and address are this one's.
    /// </summary>
    internal RaskEndpointExtensions.AttachOutcome Told(RaskEndpointExtensions.AttachOutcome attach)
    {
        if (attach is { Status: RaskEndpointExtensions.AttachStatus.Attached, Session: { } session })
        {
            Tell(session);
        }

        return attach;
    }

    /// <summary>Tells <paramref name="session" />'s reader what this request saw.</summary>
    internal void Tell(LiveSessionBase session)
    {
        if (session.Services.GetService<Reader>() is { } reader)
        {
            Tell(reader);
        }
    }

    private void Tell(Reader reader) => reader.Saw(Zone, Address);
}
