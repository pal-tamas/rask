using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     <c>Current.Service&lt;T&gt;()</c> hands a handler its event's own scoped services, the session's own where they
///     are the session's, and an endpoint its request's.
/// </summary>
public class CurrentServiceTests
{
    [Fact]
    public async Task One_event_gets_one_instance_even_across_an_await()
    {
        await using var page = await OpenAsync();

        await page.ClickAsync();

        var seen = Assert.Single(page.Log.Seen);
        Assert.Same(seen.First, seen.AfterAwait);
    }

    [Fact]
    public async Task Two_events_get_two_instances_each_disposed_as_its_event_ends()
    {
        await using var page = await OpenAsync();

        await page.ClickAsync();
        await page.ClickAsync();

        Assert.Equal(2, page.Log.Seen.Count);
        Assert.NotSame(page.Log.Seen[0].First, page.Log.Seen[1].First);
        await WaitFor.True(() => page.Log.Seen.TrueForAll(s => s.First.Disposed), "each event's scope to be disposed");
        Assert.Null(page.Session.OpenedEventScope);
    }

    [Fact]
    public async Task A_service_of_the_session_s_is_the_session_s_own()
    {
        await using var page = await OpenAsync();

        await page.ClickAsync();

        Assert.Same(page.Session.Services.GetRequiredService<IToaster>(), page.Log.Seen[0].Toaster);
    }

    [Fact]
    public async Task An_event_that_does_not_ask_opens_no_scope()
    {
        await using var page = await OpenAsync();

        await page.ClickAsync("nothing");

        Assert.Equal(0, page.Log.Created);
    }

    [Fact]
    public async Task An_endpoint_gets_its_request_s_own_instance()
    {
        using var host = RaskTestHost.Create<CurrentServiceApp>(
            Register,
            app => app.Map("/probe", branch => branch.Run(static async context =>
            {
                var same = ReferenceEquals(Current.Service<CurrentServiceProbe>(), context.RequestServices.GetRequiredService<CurrentServiceProbe>());
                await context.Response.WriteAsync(same ? "same" : "different");
            })));

        var answer = await host.Http.GetStringAsync("/probe", TestContext.Current.CancellationToken);

        Assert.Equal("same", answer);
    }

    private static void Register(IServiceCollection services)
    {
        services.AddSingleton<CurrentServiceLog>();
        services.AddScoped<CurrentServiceProbe>();
    }

    private static async Task<OpenPage> OpenAsync()
    {
        var host = RaskTestHost.Create<CurrentServiceApp>(Register);
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        var session = host.Store.Get(sessionId)!;
        await session.LastHandlerTask;
        return new OpenPage(host, ws, session, host.Server.Services.GetRequiredService<CurrentServiceLog>(), html);
    }

    private sealed class OpenPage(
        RaskTestHost host,
        System.Net.WebSockets.WebSocket ws,
        LiveSession session,
        CurrentServiceLog log,
        string html) : IAsyncDisposable
    {
        public LiveSession Session => session;

        public CurrentServiceLog Log => log;

        public async Task ClickAsync(string button = "use")
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            await ws.SendJsonAsync(new { id }, ct: TestContext.Current.CancellationToken);
            await ws.SettledAsync();
            await session.LastHandlerTask;
        }

        public async ValueTask DisposeAsync()
        {
            ws.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }
}

public sealed class CurrentServiceLog
{
    private readonly Lock _gate = new();
    private int _created;

    public int Created => Volatile.Read(ref _created);

    public List<(CurrentServiceProbe First, CurrentServiceProbe AfterAwait, IToaster Toaster)> Seen { get; } = [];

    public void Born() => Interlocked.Increment(ref _created);

    public void Saw(CurrentServiceProbe first, CurrentServiceProbe afterAwait, IToaster toaster)
    {
        lock (_gate)
        {
            Seen.Add((first, afterAwait, toaster));
        }
    }
}

// The app's own scoped service: what a DbContext is.
public sealed class CurrentServiceProbe : IDisposable
{
    public CurrentServiceProbe(CurrentServiceLog log) => log.Born();

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed partial class CurrentServiceApp : Component
{
    protected override Component? HeadAssets => Title["current"];

    protected override Component? Render() =>
        Div[
            Button.OnClick(static async () =>
            {
                var first = Current.Service<CurrentServiceProbe>();
                await Task.Yield();
                Current.Service<CurrentServiceLog>().Saw(first, Current.Service<CurrentServiceProbe>(), Current.Service<IToaster>());
            })["use"],
            Button.OnClick(static () => { })["nothing"]
        ];
}
