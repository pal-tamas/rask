using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Server.JSInterop;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     Each live event gets the app's scoped services fresh, as a request would: its own instances, signed in as the
///     user the session revalidated for that event, disposed when the event has rendered.
/// </summary>
public class EventScopeTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Two_clicks_get_two_different_instances_of_a_scoped_service(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.ClickAsync();
        await page.ClickAsync();

        Assert.Equal(2, page.Log.Seen.Count);
        Assert.NotSame(page.Log.Seen[0].Probe, page.Log.Seen[1].Probe);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_scoped_service_sees_the_user_the_session_revalidated_for_that_event(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport, signedIn: "alice");

        await page.ClickAsync();
        page.Revalidator.Role = "editor";
        await page.ClickAsync();

        Assert.Equal(["alice", "alice editor"], page.Log.Seen.Select(s => s.User));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task The_event_s_scope_is_disposed_once_the_event_has_rendered(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.ClickAsync();

        Assert.True(page.Log.Seen[0].Probe.Disposed);
        Assert.Null(page.Session.OpenedEventScope);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task An_event_that_resolves_nothing_of_the_app_s_opens_no_scope(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.ClickAsync("nothing");

        Assert.Empty(page.Log.Seen);
        Assert.Equal(0, page.Log.Created);
    }

    // Over the HTTP stream each event arrives in a POST, an HTTP request whose own services the request ambient
    // would hand out — anonymous, and not the session's — were the event's scope not the one asked first.
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_scoped_service_made_for_an_event_is_handed_the_session_s_own_user_toaster_and_route(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.ClickAsync();

        var seen = page.Log.Seen[0];
        Assert.Same(page.Session.Services.GetRequiredService<IUserProvider>(), seen.Users);
        Assert.Same(page.Session.Services.GetRequiredService<IToaster>(), seen.Toaster);
        Assert.Same(page.Session.Services.GetRequiredService<RouteState>(), seen.Route);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task The_session_s_connection_outlives_the_event_that_was_handed_it(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.ClickAsync();
        await page.ClickAsync();

        Assert.Equal(2, page.Log.Seen.Count);
        Assert.NotNull(page.Session.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>());
        Assert.IsNotType<RaskJSRuntime>(page.Log.Seen[0].Js);
    }

    private static ClaimsPrincipal Person(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)], "Test"));

    // Every request the tab makes — the page, the socket or stream, each post — carries the browser's credential for
    // signedIn, as a cookie would. Over the stream an event's post is itself a request, signed in without the role
    // the revalidator adds: only the session's answer has it.
    private static async Task<OpenPage> OpenAsync(LiveTransportKind transport, string? signedIn = null)
    {
        var revalidator = new RoleRevalidator();
        var host = RaskTestHost.Create<EventScopeApp>(
            services =>
            {
                services.AddSingleton<EventScopeLog>();
                services.AddScoped<EventScopeProbe>();
                services.AddSingleton<ISessionRevalidator>(revalidator);
            },
            app => app.Use((ctx, next) =>
            {
                if (signedIn is not null)
                {
                    ctx.User = Person(signedIn);
                }

                return next(ctx);
            }));
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await LiveTestConnection.OpenAsync(host, transport, session.Id);

        // Attached is not settled: the attach still asks about the user its connection carries and adopts the
        // answer, and a test that changes that answer before then is raced by it.
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        var log = host.Server.Services.GetRequiredService<EventScopeLog>();

        // What the GET's render opened (nothing) is not a click's.
        log.Reset();
        return new OpenPage(host, connection, session, log, revalidator, html);
    }

    private sealed class OpenPage(
        RaskTestHost host,
        ILiveTestConnection connection,
        LiveSession session,
        EventScopeLog log,
        RoleRevalidator revalidator,
        string html) : IAsyncDisposable
    {
        public LiveSession Session => session;

        public EventScopeLog Log => log;

        public RoleRevalidator Revalidator => revalidator;

        public async Task ClickAsync(string button = "use")
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            await connection.SendJsonAsync(new { id });
            await connection.SettledAsync();
            await session.LastHandlerTask;
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            await host.StopAsync();
            host.Dispose();
        }
    }

    internal sealed class RoleRevalidator : ISessionRevalidator
    {
        // The same user with another right: a principal the session takes in place of the one it held.
        public string? Role { get; set; }

        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            if (Role is null)
            {
                return ValueTask.FromResult<ClaimsPrincipal?>(principal);
            }

            var identity = new ClaimsIdentity(principal.Claims, "Test");
            identity.AddClaim(new Claim(ClaimTypes.Role, Role));
            return ValueTask.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
        }
    }
}

public sealed class EventScopeLog
{
    private readonly Lock _gate = new();
    private int _created;

    public List<(EventScopeProbe Probe, string? User, IUserProvider Users, IToaster Toaster, RouteState Route, Microsoft.JSInterop.IJSRuntime Js)> Seen { get; } = [];

    public int Created => Volatile.Read(ref _created);

    public void Born() => Interlocked.Increment(ref _created);

    public void Saw(EventScopeProbe probe, string? user, IUserProvider users, IToaster toaster, RouteState route, Microsoft.JSInterop.IJSRuntime js)
    {
        lock (_gate)
        {
            Seen.Add((probe, user, users, toaster, route, js));
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Seen.Clear();
            _created = 0;
        }
    }
}

// The app's own scoped service: what a DbContext or a repository is.
public sealed class EventScopeProbe : IDisposable
{
    private readonly IUserProvider _users;
    private readonly IToaster _toaster;
    private readonly RouteState _route;
    private readonly Microsoft.JSInterop.IJSRuntime _js;
    private readonly EventScopeLog _log;

    public EventScopeProbe(IUserProvider users, IToaster toaster, RouteState route, Microsoft.JSInterop.IJSRuntime js, EventScopeLog log)
    {
        _users = users;
        _toaster = toaster;
        _route = route;
        _js = js;
        _log = log;
        log.Born();
    }

    public bool Disposed { get; private set; }

    public void Use()
    {
        var user = _users.Current;
        _log.Saw(this, user.IsInRole("editor") ? $"{user.Identity?.Name} editor" : user.Identity?.Name, _users, _toaster, _route, _js);
    }

    public void Dispose() => Disposed = true;
}

public sealed partial class EventScopeApp : Component
{
    protected override Component? HeadAssets => Title["scope"];

    protected override Component? Render() =>
        Div[
            Button.OnClick(static () => Ambient.Services!.GetRequiredService<EventScopeProbe>().Use())["use"],
            Button.OnClick(static () => { })["nothing"]
        ];
}
