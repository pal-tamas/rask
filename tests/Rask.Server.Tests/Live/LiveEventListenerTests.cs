using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Cqrs;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     A component that implements <see cref="IEventHandler{TEvent}" /> hears a published event once per open page
///     that has it mounted — in that page's session, after what the page is doing, as its user, rendered once.
/// </summary>
public class LiveEventListenerTests
{
    [Fact]
    public async Task A_published_event_reaches_the_listener_of_every_open_page_as_that_page_s_user()
    {
        using var host = CreateHost();
        await using var alice = await OpenAsync(host, "alice");
        await using var bob = await OpenAsync(host, "bob");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();

        await Task.Run(() => Publish(host, new BoardChanged(1)), TestContext.Current.CancellationToken);
        await alice.Ws.ReceiveUntilAsync(static f => f.Contains("heard=1", StringComparison.Ordinal), "alice's page hears it");
        await bob.Ws.ReceiveUntilAsync(static f => f.Contains("heard=1", StringComparison.Ordinal), "bob's page hears it");

        Assert.Equal(["alice", "bob"], log.HeardBy.Order(StringComparer.Ordinal));
    }

    // What CI once saw as "nobody" was this: the test signed the page in by hand before the attach had adopted its
    // connection's own principal (anonymous), which then replaced it. A delivery runs as the user the page holds
    // when it runs — never one it held before.
    [Fact]
    public async Task A_delivery_runs_as_the_user_the_page_holds_when_it_runs()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "bob");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();

        await Publish(host, new BoardChanged(1));
        await WaitFor.True(() => log.HeardBy.Count == 1, "the page hears it as bob");
        page.Session.Users.Set(Person("carol"));
        await Publish(host, new BoardChanged(2));
        await WaitFor.True(() => log.HeardBy.Count == 2, "the page hears it as carol");

        Assert.Equal(["bob", "carol"], log.HeardBy);
    }

    [Fact]
    public async Task A_listener_waits_for_the_click_its_page_is_handling()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "alice");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();

        await page.ClickAsync("hold");
        await log.ClickReached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Publish(host, new BoardChanged(1));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var heardWhileHeld = log.HeardBy.Count;
        log.Open.TrySetResult();
        await WaitFor.True(() => log.HeardBy.Count == 1, "the listener runs once the click is done");

        Assert.Equal(0, heardWhileHeld);
        Assert.Equal(["click done", "heard"], log.Order);
    }

    [Fact]
    public async Task A_delivery_renders_its_page_once()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "alice");

        await Publish(host, new BoardChanged(1));
        var frames = await page.Ws.SettledAsync();

        Assert.Single(frames, static f => f.Contains("heard=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_listener_that_has_left_its_page_hears_nothing()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "alice");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();
        await page.ClickAsync("hide");

        await Publish(host, new BoardChanged(1));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await page.Session.LastHandlerTask;

        Assert.Empty(log.HeardBy);
    }

    [Fact]
    public async Task A_listening_component_is_never_a_service_handler_and_runs_once_per_page()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "alice");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();
        using var scope = host.Server.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<BoardChanged>>().ToList();

        await Publish(host, new BoardChanged(1));
        await WaitFor.True(() => log.HeardBy.Count == 1, "the page hears it");

        Assert.Equal([typeof(BoardServiceHandler)], handlers.Select(static h => h.GetType()));
        Assert.Equal(1, log.ServiceHandled);
        Assert.Single(log.HeardBy);
    }

    [Fact]
    public async Task Each_delivery_gets_scoped_services_of_its_own()
    {
        using var host = CreateHost();
        await using var page = await OpenAsync(host, "alice");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();

        await Publish(host, new BoardChanged(1));
        await WaitFor.True(() => log.Scoped.Count == 1, "the first is heard");
        await Publish(host, new BoardChanged(2));
        await WaitFor.True(() => log.Scoped.Count == 2, "the second is heard");

        Assert.NotSame(log.Scoped[0], log.Scoped[1]);
        Assert.True(log.Scoped[0].Disposed);
    }

    [Fact]
    public async Task A_listener_that_throws_leaves_the_other_pages_and_its_own_next_event_alone()
    {
        using var host = CreateHost();
        await using var alice = await OpenAsync(host, "alice");
        await using var bob = await OpenAsync(host, "bob");
        var log = host.Server.Services.GetRequiredService<ListenerLog>();
        log.ThrowFor = "alice";

        await Publish(host, new BoardChanged(1));
        await bob.Ws.ReceiveUntilAsync(static f => f.Contains("heard=1", StringComparison.Ordinal), "bob's page hears it");
        await alice.Ws.ReceiveUntilAsync(static f => f.Contains("That did not work", StringComparison.Ordinal), "alice's page shows the fault");
        log.ThrowFor = null;
        await Publish(host, new BoardChanged(2));
        await alice.Ws.ReceiveUntilAsync(static f => f.Contains("heard=1", StringComparison.Ordinal), "alice's page hears the next");
        await WaitFor.True(() => log.HeardBy.Count == 3, "bob's page hears the next too");

        Assert.Equal(["alice", "bob", "bob"], log.HeardBy.Order(StringComparer.Ordinal));
    }

    private static Task Publish(RaskTestHost host, BoardChanged e) =>
        host.Server.Services.GetRequiredService<IDispatcher>().Publish(e);

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<ListenerApp>(services =>
        {
            services.AddRaskCqrs();
            services.AddSingleton<ListenerLog>();
            services.AddScoped<ListenerScoped>();
        });

    private static async Task<OpenPage> OpenAsync(RaskTestHost host, string user)
    {
        var (ws, session, html) = await AttachAsync(host);

        // Once the attach has adopted the connection's own (anonymous) principal: set before, it would be replaced.
        session.Users.Set(Person(user));
        await ws.SettledAsync();
        return new OpenPage(ws, session, html);
    }

    private static async Task<(WebSocket Ws, LiveSession Session, string Html)> AttachAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var session = host.Store.Peek(sessionId)!;
        var settled = session.AttachesSettled;
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        return (ws, session, html);
    }

    private static ClaimsPrincipal Person(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "Test"));

    private sealed class OpenPage(WebSocket ws, LiveSession session, string html) : IAsyncDisposable
    {
        public WebSocket Ws => ws;

        public LiveSession Session => session;

        public async Task ClickAsync(string button)
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            await ws.SendJsonAsync(new { id }, ct: TestContext.Current.CancellationToken);
            if (button != "hold")
            {
                await ws.SettledAsync();
                await session.LastHandlerTask;
            }
        }

        public ValueTask DisposeAsync()
        {
            ws.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record BoardChanged(int Board) : IEvent;

public sealed class ListenerLog
{
    private readonly Lock _gate = new();
    private int _serviceHandled;

    public List<string> HeardBy { get; } = [];

    public List<string> Order { get; } = [];

    public List<ListenerScoped> Scoped { get; } = [];

    public string? ThrowFor { get; set; }

    public int ServiceHandled => Volatile.Read(ref _serviceHandled);

    public TaskCompletionSource ClickReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Note(string entry)
    {
        lock (_gate)
        {
            Order.Add(entry);
        }
    }

    public void Heard(string user, ListenerScoped scoped)
    {
        lock (_gate)
        {
            HeardBy.Add(user);
            Scoped.Add(scoped);
            Order.Add("heard");
        }
    }

    public void ServiceRan() => Interlocked.Increment(ref _serviceHandled);
}

public sealed class ListenerScoped : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

// The service handler of the same event: run once per publish, however many pages listen.
public sealed class BoardServiceHandler(ListenerLog log) : IEventHandler<BoardChanged>
{
    public Task Handle(BoardChanged e)
    {
        log.ServiceRan();
        return Task.CompletedTask;
    }
}

public sealed partial class BoardListener(ListenerLog log, IUserProvider users) : Component, IEventHandler<BoardChanged>
{
    private int _heard;

    public async Task Handle(BoardChanged e)
    {
        await Task.Yield();
        var user = users.Current.Identity?.Name ?? "nobody";
        if (user == log.ThrowFor)
        {
            log.Note("threw");
            throw new InvalidOperationException("the board could not be read");
        }

        _heard++;
        log.Heard(user, Ambient.Services!.GetRequiredService<ListenerScoped>());
    }

    protected override Component? Render() => Span[$"heard={_heard}"];
}

public sealed partial class ListenerApp(ListenerLog log) : Component
{
    private bool _showBoard = true;

    protected override Component? HeadAssets => Title["listeners"];

    protected override Component? Render() =>
        Div[
            _showBoard ? BoardListener : null,
            Button.OnClick(() => _showBoard = false)["hide"],
            Button.OnClick(HoldAsync)["hold"],
            ToastOutlet.Template(static (toasts, _) => Div[toasts.Select(static t => Span.Key(t.Id)[t.Message])])
        ];

    private async Task HoldAsync()
    {
        log.ClickReached.TrySetResult();
        await log.Open.Task;
        log.Note("click done");
    }
}
