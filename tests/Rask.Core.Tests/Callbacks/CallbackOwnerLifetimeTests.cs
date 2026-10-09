using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // the render root is a stub built by hand, as a session builds its own

namespace Rask.Core.Tests.Callbacks;

// A callback is the code of the component that WROTE it, reached through another one. While it runs — its
// synchronous part and every continuation — the calls that take no token are cancelled with its writer,
// whoever invoked it: a page's OnSaved closes the editor that raised it, and then reloads.
public sealed partial class CallbackOwnerLifetimeTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(OwnedShape.Async)]
    [InlineData(OwnedShape.Typed)]
    [InlineData(OwnedShape.Forwarded)]
    [InlineData(OwnedShape.Relayed)]
    [InlineData(OwnedShape.AfterUnmount)]
    [InlineData(OwnedShape.Passed)]
    public async Task A_callback_that_unmounts_its_invoker_finishes_both_reads_under_its_writers_lifetime(OwnedShape shape)
    {
        using var turn = new Turn(shape);

        await turn.Click();
        await turn.Shows("closed rows:6");

        Assert.Null(turn.Probe.Fault);
        Assert.Equal(turn.Probe.Owner, turn.Probe.AtStart);
        Assert.Equal(turn.Probe.Owner, turn.Probe.OwnProperty);
        Assert.Equal(turn.Probe.Owner, turn.Probe.AfterClose);
        Assert.False(turn.Probe.AfterClose.IsCancellationRequested);
    }

    [Theory]
    [InlineData(OwnedShape.Async)]
    [InlineData(OwnedShape.Sync)]
    [InlineData(OwnedShape.Typed)]
    [InlineData(OwnedShape.Relayed)]
    public async Task The_invokers_handler_goes_on_under_its_own_lifetime_once_the_callback_returns(OwnedShape shape)
    {
        using var turn = new Turn(shape);

        await turn.Click();
        await turn.Shows("closed");

        Assert.Equal(turn.Probe.Child, turn.Probe.ChildAfter);
        Assert.True(turn.Probe.ChildAfter.IsCancellationRequested);
        Assert.False(Ambient.CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task A_synchronous_callback_runs_under_its_writers_lifetime()
    {
        using var turn = new Turn(OwnedShape.Sync);

        await turn.Click();
        await turn.Shows("closed");

        Assert.Equal(turn.Probe.Owner, turn.Probe.AtStart);
        Assert.Equal(turn.Probe.Owner, turn.Probe.OwnProperty);
    }

    [Fact]
    public async Task A_callback_raised_from_a_childs_lifecycle_hook_runs_under_its_writers_lifetime()
    {
        using var turn = new Turn(OwnedShape.FromMount);

        await turn.Shows("closed rows:6");

        Assert.Null(turn.Probe.Fault);
        Assert.Equal(turn.Probe.Owner, turn.Probe.AtStart);
        Assert.Equal(turn.Probe.Owner, turn.Probe.AfterClose);
        Assert.NotEqual(turn.Probe.Owner, turn.Probe.ChildAfter);
    }

    [Fact]
    public async Task A_callback_raised_after_its_invoker_was_unmounted_still_runs()
    {
        using var turn = new Turn(OwnedShape.AfterUnmount);

        await turn.Click();
        await turn.Shows("closed rows:6");

        Assert.True(turn.Probe.ChildGoneBeforeRaising);
        Assert.Equal(turn.Probe.Owner, turn.Probe.AtStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_callback_whose_writer_leaves_the_page_is_cancelled_and_trips_no_error_boundary(bool wrapped)
    {
        using var turn = new Turn(OwnedShape.Held, wrapCancellation: wrapped);
        var click = turn.Click();
        await turn.Probe.Entered;

        turn.Leave();
        turn.Probe.Release();
        var escaped = await Record.ExceptionAsync(() => click);

        Assert.True(turn.Probe.AtStart.IsCancellationRequested);
        Assert.False(turn.Probe.CancelledAfterClose);
        Assert.True(turn.Probe.CancelledAfterHold);
        Assert.Equal(wrapped ? typeof(InvalidOperationException) : typeof(OperationCanceledException), turn.Probe.Fault?.GetType());
        Assert.Equal(wrapped, escaped is null);
        Assert.DoesNotContain("boundary-tripped", turn.Render());
        Assert.Contains("gone", turn.Html);
    }

    [Fact]
    public async Task A_fault_in_a_callback_whose_writer_is_still_shown_trips_its_error_boundary()
    {
        using var turn = new Turn(OwnedShape.Held, failReads: true);
        var click = turn.Click();
        await turn.Probe.Entered;

        turn.Probe.Release();
        await click;

        Assert.Contains("boundary-tripped", turn.Html);
    }

    [Fact]
    public async Task Under_a_handler_timeout_a_callback_answers_to_its_writer_and_to_the_timeout()
    {
        using var timeout = new CancellationTokenSource();
        using var turn = new Turn(OwnedShape.Held) { HandlerTimeout = timeout.Token };
        var click = turn.Click();
        await turn.Probe.Entered;

        await timeout.CancelAsync();
        turn.Probe.Release();
        var escaped = await Record.ExceptionAsync(() => click);

        Assert.NotEqual(turn.Probe.Owner, turn.Probe.AtStart);
        Assert.Equal(turn.Probe.AtStart, turn.Probe.OwnProperty);
        Assert.False(turn.Probe.CancelledAfterClose);
        Assert.True(turn.Probe.CancelledAfterHold);
        Assert.False(turn.Probe.Owner.IsCancellationRequested);
        Assert.IsAssignableFrom<OperationCanceledException>(escaped);
    }

    [Fact]
    public async Task Under_a_handler_timeout_a_callback_is_cancelled_when_its_writer_leaves_the_page()
    {
        using var timeout = new CancellationTokenSource();
        using var turn = new Turn(OwnedShape.Held) { HandlerTimeout = timeout.Token };
        var click = turn.Click();
        await turn.Probe.Entered;

        turn.Leave();
        turn.Probe.Release();
        await Record.ExceptionAsync(() => click);

        Assert.False(turn.Probe.CancelledAfterClose);
        Assert.True(turn.Probe.CancelledAfterHold);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Fact]
    public async Task Under_a_handler_timeout_a_callback_that_unmounts_its_invoker_finishes_both_reads()
    {
        using var timeout = new CancellationTokenSource();
        using var turn = new Turn(OwnedShape.Async) { HandlerTimeout = timeout.Token };

        await turn.Click();
        await turn.Shows("closed rows:6");

        Assert.Null(turn.Probe.Fault);
        Assert.False(turn.Probe.CancelledAfterClose);
        Assert.True(turn.Probe.ChildAfter.IsCancellationRequested);
        Assert.False(Ambient.CancellationToken.CanBeCanceled);
    }

    [Fact]
    public void A_synchronous_callback_that_throws_hands_the_invokers_token_back()
    {
        var owner = OwnedCounter;
        var callback = AutoCallback.Wrap((Action)owner.Fail)!;
        using var invoker = new CancellationTokenSource();
        using var work = Ambient.Enter(invoker.Token);

        var fault = Record.Exception(callback);

        Assert.IsType<InvalidOperationException>(fault);
        Assert.Equal(invoker.Token, Ambient.CancellationToken);
    }

    [Fact]
    public async Task An_asynchronous_callback_hands_the_invokers_token_back_while_it_is_still_running()
    {
        var owner = OwnedCounter;
        var callback = AutoCallback.Wrap((Func<Task>)owner.Wait)!;
        using var invoker = new CancellationTokenSource();
        using var work = Ambient.Enter(invoker.Token);

        var running = callback();
        var whileRunning = Ambient.CancellationToken;
        owner.Release();
        await running;

        Assert.Equal(invoker.Token, whileRunning);
        Assert.Equal(invoker.Token, Ambient.CancellationToken);
        Assert.Equal(owner.LifetimeTokenInternal, owner.SeenAfterWaiting);
    }

    [Fact]
    public async Task An_asynchronous_callback_that_is_cancelled_hands_the_invokers_token_back()
    {
        var owner = OwnedCounter;
        var callback = AutoCallback.Wrap((Func<Task>)owner.Cancelled)!;
        using var invoker = new CancellationTokenSource();
        using var work = Ambient.Enter(invoker.Token);

        var fault = await Record.ExceptionAsync(callback);

        Assert.IsAssignableFrom<OperationCanceledException>(fault);
        Assert.Equal(invoker.Token, Ambient.CancellationToken);
    }

    [Fact]
    public async Task Every_shape_of_callback_runs_under_its_writers_lifetime()
    {
        var owner = OwnedCounter;
        using var invoker = new CancellationTokenSource();
        using var work = Ambient.Enter(invoker.Token);
        Func<Task>[] shapes =
        [
            () => Now(AutoCallback.Wrap((Action)owner.Bump)!),
            () => Now(() => AutoCallback.Wrap((Action<int>)owner.Bump)!(1)),
            () => Now(() => AutoCallback.Wrap((Action<int, int>)owner.Bump)!(1, 2)),
            () => Now(() => ((Action<object?>)AutoCallback.Wrap((Delegate)(Action<int>)owner.Bump)!)(1)),
            () => AutoCallback.Wrap((Func<Task>)owner.BumpLater)!(),
            () => AutoCallback.Wrap((Func<int, Task>)owner.BumpLater)!(1),
            () => AutoCallback.Wrap((Func<int, int, Task>)owner.BumpLater)!(1, 2),
            () => ((Func<object?, Task>)AutoCallback.Wrap((Delegate)(Func<int, Task>)owner.BumpLater)!)(1),
            () => AutoCallback.Wrap((Func<ValueTask>)owner.BumpSoon)!().AsTask(),
            () => AutoCallback.Wrap((Func<int, ValueTask>)owner.BumpSoon)!(1).AsTask(),
            () => AutoCallback.Wrap((Func<int, int, ValueTask>)owner.BumpSoon)!(1, 2).AsTask(),
        ];

        List<CancellationToken> seen = [];
        foreach (var raise in shapes)
        {
            await raise();
            seen.Add(owner.TakeSeen());
        }

        Assert.All(seen, token => Assert.Equal(owner.LifetimeTokenInternal, token));
        Assert.Equal(invoker.Token, Ambient.CancellationToken);
    }

    private static Task Now(Action raise)
    {
        raise();
        return Task.CompletedTask;
    }

    [Fact]
    public void A_callback_raised_under_its_writers_lifetime_allocates_nothing()
    {
        var owner = OwnedCounter;
        var callback = AutoCallback.Wrap((Action)owner.Bump)!;
        using var work = Ambient.Enter(owner.LifetimeTokenInternal);
        callback();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            callback();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(101, owner.Count);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_callback_of_a_writer_that_left_the_page_sees_a_cancelled_token()
    {
        var owner = OwnedCounter;
        var callback = AutoCallback.Wrap((Action)owner.Bump)!;
        var shown = true;
        var root = new StubComponent(() => shown ? owner : Span["gone"]);
        root.RenderAsLiveRoot(RenderHarness.EmptyServices());

        shown = false;
        root.RenderAsLiveRoot(RenderHarness.EmptyServices());
        callback();

        Assert.True(owner.Seen.IsCancellationRequested);
    }

    // What a session is to a handler: the services, the navigator's handler scope, and a render after it.
    private sealed class Turn : IRenderHandle, IDisposable
    {
        private readonly Lock _render = new();
        private readonly ServiceProvider _services;
        private readonly StubComponent _view;
        private bool _gone;

        public Turn(OwnedShape shape, bool wrapCancellation = false, bool failReads = false)
        {
            Probe = new OwnedProbe(shape, wrapCancellation, failReads);
            _services = new ServiceCollection().AddSingleton(Probe).BuildServiceProvider();
            _view = new StubComponent(() =>
                ErrorBoundary.Fallback((_, _) => Span["boundary-tripped"])[_gone ? Span["gone"] : OwnedPage])
            {
                RenderHandle = this,
            };
            Render();
        }

        public OwnedProbe Probe { get; }

        public CancellationToken HandlerTimeout { get; init; }

        public string Html { get; private set; } = "";

        public string Render()
        {
            lock (_render)
            {
                return Html = _view.RenderAsLiveRoot(_services);
            }
        }

        public void Leave()
        {
            _gone = true;
            Render();
        }

        public async Task Click()
        {
            using var payload = JsonDocument.Parse("{}");
            await _view.TryInvokeHandlerAsync(
                MarkupAssert.Attr(Html, "data-rask-on-click")!, payload.RootElement, _services, HandlerTimeout);
            Render();
        }

        public Task Shows(string text) =>
            WaitFor.True(() => Html.Contains(text, StringComparison.Ordinal), $"'{text}' in: {Html}");

        public Task RequestRender()
        {
            Render();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync() => RequestRender();

        public void Dispose() => _services.Dispose();
    }
}

public enum OwnedShape
{
    Async,
    Sync,
    Typed,
    Forwarded,
    Relayed,
    FromMount,
    AfterUnmount,
    Passed,
    Held,
}

public sealed class OwnedProbe(OwnedShape shape, bool wrapCancellation, bool failReads)
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OwnedShape Shape { get; } = shape;
    public CancellationToken Owner { get; set; }
    public CancellationToken Child { get; set; }
    public CancellationToken AtStart { get; set; }
    public CancellationToken OwnProperty { get; set; }
    public CancellationToken AfterClose { get; set; }
    public CancellationToken ChildAfter { get; set; }
    public bool CancelledAfterClose { get; set; }
    public bool CancelledAfterHold { get; set; }
    public bool ChildGoneBeforeRaising { get; set; }
    public Exception? Fault { get; set; }

    public Task Entered => _entered.Task;

    public void Release() => _gate.TrySetResult();

    // Where a test steps in: it unmounts the writer, or lets the timeout pass, before the reads go on.
    public Task Hold()
    {
        _entered.TrySetResult();
        return Shape == OwnedShape.Held ? _gate.Task : Task.CompletedTask;
    }

    // A save: its own async method, whose continuation carries the flow onto the pool.
    public static async Task Save() => await Task.Delay(1).ConfigureAwait(false);

    // A read as a data call makes it: no token passed, so it is cancelled with the work in progress — and a
    // provider that wraps the cancellation, as EF's execution strategy does, reports a failure instead.
    public async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Yield();
        if (failReads)
        {
            throw new InvalidOperationException("the database is down");
        }

        if (token.IsCancellationRequested && wrapCancellation)
        {
            throw new InvalidOperationException(
                "An exception has been raised that is likely due to a transient failure.",
                new OperationCanceledException(token));
        }

        token.ThrowIfCancellationRequested();
        return 3;
    }
}

// The writer: every callback below is its code.
internal sealed partial class OwnedPage(OwnedProbe probe) : Component
{
    private bool _closed;
    private int _rows;

    protected override Component? Render()
    {
        probe.Owner = LifetimeTokenInternal;
        if (_closed)
        {
            return Span[$"closed rows:{_rows}"];
        }

        return probe.Shape switch
        {
            OwnedShape.Sync => OwnedEditor.OnSaved(SavedAtOnce),
            OwnedShape.Typed => OwnedEditor.OnPicked(Picked),
            OwnedShape.Forwarded => OwnedFrame.OnSaved(Saved),
            OwnedShape.Relayed => OwnedFrame.Relays(true).OnSaved(Saved),
            OwnedShape.FromMount => OwnedLoader.OnLoaded(Saved),
            OwnedShape.AfterUnmount => OwnedEditor.OnClosed(Close).OnSaved(Saved),
            OwnedShape.Passed => OwnedButton.OnPress(Saved),
            _ => OwnedEditor.OnSaved(Saved),
        };
    }

    private void Close() => _closed = true;

    private void SavedAtOnce()
    {
        probe.AtStart = Current.Cancellation;
        probe.OwnProperty = CancellationToken;
        _closed = true;
    }

    private Task Picked(int rows) => Saved();

    // Closes the component that raised it, then reloads: two reads that pass no token.
    private async Task Saved()
    {
        probe.AtStart = Current.Cancellation;
        probe.OwnProperty = CancellationToken;
        _closed = true;
        StateHasChanged();
        try
        {
            await OwnedProbe.Save();
            probe.CancelledAfterClose = Current.Cancellation.IsCancellationRequested;
            await probe.Hold();
            probe.CancelledAfterHold = Current.Cancellation.IsCancellationRequested;
            _rows = await probe.Read();
            _rows += await probe.Read();
        }
        catch (Exception ex)
        {
            probe.Fault = ex;
            throw;
        }

        probe.AfterClose = Current.Cancellation;
    }
}

internal sealed partial class OwnedEditor(OwnedProbe probe) : Component
{
    public Callback OnSaved { get; set; }

    public Callback<int> OnPicked { get; set; }

    public Callback OnClosed { get; set; }

    protected override Component? Render() => Button.OnClick(Save)["save"];

    private async Task Save()
    {
        probe.Child = LifetimeTokenInternal;
        await OnClosed.Invoke();
        probe.ChildGoneBeforeRaising = IsTornDown;
        await OnPicked.Invoke(3);
        await OnSaved.Invoke();
        probe.ChildAfter = Current.Cancellation;
    }
}

// Between the writer and the component that raises: it hands the callback on, as it got it or in a lambda.
internal sealed partial class OwnedFrame : Component
{
    public Callback OnSaved { get; set; }

    public bool? Relays { get; set; }

    protected override Component? Render() =>
        Relays == true ? OwnedEditor.OnSaved(() => OnSaved.Invoke()) : OwnedEditor.OnSaved(OnSaved);
}

// Raises from a lifecycle hook, where no handler pushed a lifetime at all.
internal sealed partial class OwnedLoader(OwnedProbe probe) : Component
{
    public Callback OnLoaded { get; set; }

    protected override async Task OnMount()
    {
        probe.Child = LifetimeTokenInternal;
        await OwnedProbe.Save();
        await OnLoaded.Invoke();
        probe.ChildAfter = Current.Cancellation;
    }

    protected override Component? Render() => Span["loading"];
}

// Hands the callback straight to an element, as a kit button does: the dispatch already runs it for its writer.
internal sealed partial class OwnedButton : Component
{
    public Callback OnPress { get; set; }

    protected override Component? Render() => Button.OnClick(OnPress)["press"];
}

internal sealed partial class OwnedCounter : Component
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int Count { get; private set; }

    internal CancellationToken Seen { get; private set; }

    internal CancellationToken SeenAfterWaiting { get; private set; }

    protected override Component? Render() => Span[Count.ToString(System.Globalization.CultureInfo.InvariantCulture)];

    internal void Bump()
    {
        Count++;
        Seen = Current.Cancellation;
    }

    internal void Bump(int by) => Bump();

    internal void Bump(int by, int again) => Bump();

    internal async Task BumpLater()
    {
        await Task.Yield();
        Bump();
    }

    internal Task BumpLater(int by) => BumpLater();

    internal Task BumpLater(int by, int again) => BumpLater();

    internal async ValueTask BumpSoon() => await BumpLater();

    internal ValueTask BumpSoon(int by) => BumpSoon();

    internal ValueTask BumpSoon(int by, int again) => BumpSoon();

    // What the last callback saw, forgotten so the next shape cannot pass on its predecessor's reading.
    internal CancellationToken TakeSeen()
    {
        var seen = Seen;
        Seen = default;
        return seen;
    }

    internal void Fail() => throw new InvalidOperationException("no");

    internal void Release() => _released.TrySetResult();

    internal async Task Wait()
    {
        await _released.Task.ConfigureAwait(false);
        SeenAfterWaiting = Current.Cancellation;
    }

    internal async Task Cancelled()
    {
        await Task.Yield();
        throw new OperationCanceledException();
    }
}
