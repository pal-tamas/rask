using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A page whose component handles an event, in a real browser: a timer on another thread publishes it, and the
///     page shows what its handler did with no click and no reload.
/// </summary>
public sealed class LiveEventListenerTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task An_event_a_timer_publishes_is_drawn_on_the_open_page()
    {
        await using var session = await HookSession.OpenAsync<TickerPage>(
            playwright, services: static services => services.AddRaskCqrs());
        var page = session.Page;

        await page.ClickAsync("#start");

        await Expect(page.Locator("#ticks")).ToHaveTextAsync("ticks=3");
    }
}

public sealed record Ticked(int Number) : IEvent;

/// <summary>Starts a timer that publishes three ticks from the thread pool, and counts the ticks it hears.</summary>
public sealed partial class TickerPage(IDispatcher dispatcher) : Component, IEventHandler<Ticked>, IDisposable
{
    private Timer? _timer;
    private int _published;
    private int _heard;

    public Task Handle(Ticked e)
    {
        _heard++;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    protected override Component? Render() =>
        Div[
            Button.Id("start").OnClick(Start)["start"],
            Span.Id("ticks")[$"ticks={_heard}"]
        ];

    private void Start() =>
        _timer ??= new Timer(_ =>
        {
            var number = Interlocked.Increment(ref _published);
            if (number <= 3)
            {
                _ = dispatcher.Publish(new Ticked(number));
            }
        }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
}
