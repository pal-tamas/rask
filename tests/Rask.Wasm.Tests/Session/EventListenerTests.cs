using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// In the browser: an event published inside the tab reaches its page's listening component, as an event of the page's
// own — after a click in flight, with that event's services, and rendered.
[Collection("WasmSession")]
public sealed class EventListenerTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _frames = [];

    public EventListenerTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task An_event_published_in_the_tab_reaches_its_page_s_listener_and_is_drawn()
    {
        var (session, services) = NewListeningSession();
        await session.InitialRenderAsync();
        var log = services.GetRequiredService<TabLog>();

        await services.GetRequiredService<IDispatcher>().Publish(new TabNotice(1), TestContext.Current.CancellationToken);
        await WaitFor(() => _frames.Exists(static f => f.Contains("notices=1", StringComparison.Ordinal)));

        Assert.Equal([1], log.Heard);
    }

    [Fact]
    public async Task A_listener_waits_for_the_click_in_flight()
    {
        var (session, services) = NewListeningSession();
        var html = Html(await session.InitialRenderAsync());
        var log = services.GetRequiredService<TabLog>();

        var click = Click(session, html, "hold");
        await log.ClickReached.Task;
        await services.GetRequiredService<IDispatcher>().Publish(new TabNotice(1), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var heardWhileHeld = log.Heard.Count;
        log.Open.TrySetResult();
        await click;
        await WaitFor(() => log.Heard.Count == 1);

        Assert.Equal(0, heardWhileHeld);
        Assert.Equal(["click done", "heard"], log.Order);
    }

    [Fact]
    public async Task Each_event_gets_scoped_services_of_its_own()
    {
        var (session, services) = NewListeningSession();
        await session.InitialRenderAsync();
        var log = services.GetRequiredService<TabLog>();
        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new TabNotice(1), TestContext.Current.CancellationToken);
        await WaitFor(() => log.Scoped.Count == 1);
        await dispatcher.Publish(new TabNotice(2), TestContext.Current.CancellationToken);
        await WaitFor(() => log.Scoped.Count == 2);

        Assert.NotSame(log.Scoped[0], log.Scoped[1]);
        Assert.True(log.Scoped[0].Disposed);
    }

    private static (WasmLiveSession Session, IServiceProvider Services) NewListeningSession() =>
        NewSession<TabApp>(configure: s =>
        {
            EventScopeCatalog.Add(s);
            s.AddRaskCqrs();
            s.AddSingleton<TabLog>();
            s.AddScoped<TabScoped>();
        });

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(10); !condition(); await Task.Delay(10))
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
        }
    }

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.TryGetProperty("html", out var html) ? html.GetString()! : doc.RootElement.GetRawText();
    }

    private static Task Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        return session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}"""));
    }
}

public sealed record TabNotice(int Number) : IEvent;

public sealed class TabLog
{
    public List<int> Heard { get; } = [];

    public List<string> Order { get; } = [];

    public List<TabScoped> Scoped { get; } = [];

    public TaskCompletionSource ClickReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class TabScoped : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed partial class TabApp(TabLog log) : Component, IEventHandler<TabNotice>
{
    public Task Handle(TabNotice e)
    {
        log.Heard.Add(e.Number);
        log.Order.Add("heard");
        log.Scoped.Add(Ambient.Services!.GetRequiredService<TabScoped>());
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Div[
            Span[$"notices={log.Heard.Count}"],
            Button.Id("hold").OnClick(HoldAsync)["hold"]
        ];

    private async Task HoldAsync()
    {
        log.ClickReached.TrySetResult();
        await log.Open.Task;
        log.Order.Add("click done");
    }
}
