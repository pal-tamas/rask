using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Transport;

namespace Rask.Server.Tests.Live;

/// <summary>
///     A page reached in place is sent at once, whatever it is still reading; a handler's page waits for its
///     reads within the handler budget, and costs a page that has nothing to wait for nothing.
/// </summary>
/// <remarks>
///     Driven at the session, over a connection that takes a frame without yielding, so the answer is exact:
///     a render that has finished by the time the call returns awaited nothing — not the budget, not a timer,
///     not one turn of the thread pool.
/// </remarks>
public sealed class ArrivalWaitTests
{
    private static readonly TimeSpan LongerThanATestMayRun = TimeSpan.FromMinutes(5);

    [Fact]
    public void A_page_with_nothing_pending_is_sent_before_the_call_returns()
    {
        var (session, transport) = Attached(new Still(), LongerThanATestMayRun);

        var sending = session.RenderAndSendAsync("/still", replace: false, arriving: true);

        Assert.True(sending.IsCompletedSuccessfully);
        Assert.Contains("/still", Assert.Single(transport.Sent), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_whose_mount_is_still_reading_is_sent_at_once_under_a_handler_budget_of_minutes()
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, transport) = Attached(new Reading(read.Task), LongerThanATestMayRun);
        session.InHandlerScope = true;

        var sending = session.RenderAndSendAsync("/reading", replace: false, arriving: true);

        Assert.True(sending.IsCompletedSuccessfully);
        Assert.Contains("reading", Assert.Single(transport.Sent), StringComparison.Ordinal);
        Assert.DoesNotContain("rows:3", transport.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_while_a_navigation_waits_its_turn_and_the_next_render_after_it_starts_is()
    {
        var (session, transport) = Attached(new Still(), LongerThanATestMayRun);
        session.NavigationRead();

        var withheld = await session.RenderAndSendAsync(null, replace: false);
        var started = session.NavigationStarts();
        var sent = await session.RenderAndSendAsync("/still", replace: false, arriving: true);

        Assert.False(withheld);
        Assert.True(started);
        Assert.True(sent);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public void A_handlers_render_with_nothing_pending_is_sent_before_the_call_returns_under_a_budget_of_minutes()
    {
        var (session, transport) = Attached(new Still(), LongerThanATestMayRun);

        var sending = session.RenderAndSendAsync(null, replace: false, settling: true);

        Assert.True(sending.IsCompletedSuccessfully);
        Assert.Contains("still", Assert.Single(transport.Sent), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handlers_render_whose_component_is_still_reading_is_held_back_until_the_read_returns()
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, transport) = Attached(new Reading(read.Task), LongerThanATestMayRun);
        session.InHandlerScope = true;

        var sending = session.RenderAndSendAsync(null, replace: false, settling: true);
        var sentWhileReading = transport.Sent.Count;
        read.SetResult();
        await sending;

        Assert.Equal(0, sentWhileReading);
        Assert.Contains("rows:3", Assert.Single(transport.Sent), StringComparison.Ordinal);
        Assert.Null(session.TakeUnsettledReads());
    }

    [Fact]
    public async Task A_handlers_render_sent_at_the_budget_leaves_the_read_it_went_without_for_the_ack_to_wait_on()
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, transport) = Attached(new Reading(read.Task), TimeSpan.FromMilliseconds(20));
        session.InHandlerScope = true;

        await session.RenderAndSendAsync(null, replace: false, settling: true);
        var left = session.TakeUnsettledReads();
        var outWhileReading = left is { IsCompleted: false };
        read.SetResult();
        await left!;

        Assert.True(outWhileReading);
        Assert.DoesNotContain("rows:3", Assert.Single(transport.Sent), StringComparison.Ordinal);
        Assert.Null(session.TakeUnsettledReads());
    }

    [Fact]
    public void A_handler_budget_of_zero_sends_a_page_at_once_though_its_mount_is_still_reading()
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, transport) = Attached(new Reading(read.Task), TimeSpan.Zero);
        session.InHandlerScope = true;

        var sending = session.RenderAndSendAsync(null, replace: false, settling: true);

        Assert.True(sending.IsCompletedSuccessfully);
        Assert.DoesNotContain("rows:3", Assert.Single(transport.Sent), StringComparison.Ordinal);
        Assert.Null(session.TakeUnsettledReads());
    }

    [Fact]
    public void A_render_nobody_dispatched_does_not_wait_for_the_reads_it_starts()
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, transport) = Attached(new Reading(read.Task), LongerThanATestMayRun);
        session.InHandlerScope = true;

        var sending = session.RenderAndSendAsync(null, replace: false);

        // A timer's render, a query's value landing: sent as it stands, as before.
        Assert.True(sending.IsCompletedSuccessfully);
        Assert.Single(transport.Sent);
    }

    private static (LiveSession Session, Sink Transport) Attached(Component view, TimeSpan budget)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RaskServerLimits { HandlerQuiescenceTimeout = budget });
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();
        var provider = services.BuildServiceProvider();
        var session = new LiveSessionStore(provider.GetRequiredService<IServiceScopeFactory>()).Create(_ => view);
        var transport = new Sink();
        session.AttachTransport(transport, CancellationToken.None);
        return (session, transport);
    }

    /// <summary>A connection that has taken a frame by the time it is asked to.</summary>
    private sealed class Sink : ILiveTransport
    {
        public bool IsOpen => true;

        public ConnectionRecord Record { get; } = new(0, "test", 0);

        public List<string> Sent { get; } = [];

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Sent.Add(System.Text.Encoding.UTF8.GetString(frame.Span));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct) => Task.CompletedTask;

        public void Abort()
        {
        }
    }

    private sealed class Still : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => Markup.H1["still"];
    }

    private sealed class Reading(Task read) : Component
    {
        private int? _rows;

        protected override string? HtmlLang => null;

        protected override async Task OnMount()
        {
            await read;
            _rows = 3;
        }

        protected override Component? Render() => Markup.P[_rows is { } rows ? $"rows:{rows}" : "reading"];
    }
}
