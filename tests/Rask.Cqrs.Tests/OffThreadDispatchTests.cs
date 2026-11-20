using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

// The page and the worker of a browser app, in one process: the page's container sends through OffThreadDispatch, and
// the line between them is a channel that hands the frame to the worker's executor — the whole path but postMessage.
public sealed class OffThreadDispatchTests
{
    [Fact]
    public async Task An_off_thread_query_is_answered_by_the_worker_in_the_pages_language()
    {
        using var app = new BrowserApp();
        var previous = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
        string answer;
        try
        {
            answer = await app.Page.Query(new WhereAmI("sheet"), TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("sheet on the worker in hu-HU", answer);
        Assert.Equal(1, app.Channel.Calls);
    }

    [Fact]
    public async Task A_byte_array_crosses_beside_the_json_rather_than_as_base64()
    {
        using var app = new BrowserApp();
        var bytes = Enumerable.Range(0, 100_000).Select(static i => (byte)(i % 251)).ToArray();

        var sum = await app.Page.Query(new SumBytes(bytes), TestContext.Current.CancellationToken);

        Assert.Equal(bytes.Sum(static b => (long)b), sum);
        var header = Header(app.Channel.LastFrame!);
        Assert.True(header.Length < 1_000, $"the header was {header.Length} bytes: the array went into the JSON");
        Assert.Matches("\"m\":\\{\"[Bb]ytes\":0\\}", header);
        Assert.Equal(bytes.Length + header.Length + sizeof(int), app.Channel.LastFrame!.Length);
    }

    [Fact]
    public async Task A_byte_array_result_comes_back_whole()
    {
        using var app = new BrowserApp();

        var reversed = await app.Page.Query(new Reversed([1, 2, 3, 250]), TestContext.Current.CancellationToken);

        Assert.Equal([250, 3, 2, 1], reversed);
    }

    [Fact]
    public async Task A_validation_failure_in_the_worker_is_the_pages_validation_exception()
    {
        using var app = new BrowserApp();

        var thrown = await Assert.ThrowsAsync<RaskValidationException>(
            () => app.Page.Send(new Refuse("Email"), TestContext.Current.CancellationToken));

        var failure = Assert.Single(thrown.Failures);
        Assert.Equal("Not like that.", failure.Message);
        Assert.Equal(["Email"], failure.Fields);
    }

    [Fact]
    public async Task Any_other_exception_in_the_worker_is_a_remote_dispatch_failure_with_its_text_and_type()
    {
        using var app = new BrowserApp();

        var thrown = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => app.Page.Send(new Break("the sheet is empty"), TestContext.Current.CancellationToken));

        Assert.Contains("the sheet is empty", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, thrown.ProblemType);
        Assert.Contains("BreakHandler", thrown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_cancels_the_handlers_token_in_the_worker_and_restarts_nothing()
    {
        // A grace no loaded runner outlasts: what is asserted is that a worker that answers is not restarted, so the test
        // waits for that answer rather than for a fixed time — 100 ms was shorter than a busy gate's thread-pool hop.
        using var app = new BrowserApp(grace: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var call = app.Page.Query(new WaitPolitely(7), cancellation.Token);
        await app.Gate.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(await app.Gate.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken));
        await app.Channel.Answered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, app.Channel.Cancels);
        Assert.Equal(0, app.Channel.Restarts);
    }

    [Fact]
    public async Task A_cancelled_handler_that_never_yields_answers_the_caller_at_once_and_has_its_worker_restarted()
    {
        using var app = new BrowserApp();
        using var cancellation = new CancellationTokenSource();
        var call = app.Page.Query(new Grind(3), cancellation.Token);
        await app.Gate.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await app.Channel.Restarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, app.Channel.Restarts);
        app.Gate.Release.Set();
    }

    [Fact]
    public async Task A_call_waiting_on_a_worker_that_restarts_fails_rather_than_hanging()
    {
        using var app = new BrowserApp();
        var call = app.Page.Query(new Grind(4), TestContext.Current.CancellationToken);
        await app.Gate.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        app.Channel.Restart();

        var thrown = await Assert.ThrowsAsync<RemoteDispatchException>(() => call);
        Assert.Contains("stopped before it answered", thrown.Message, StringComparison.Ordinal);
        app.Gate.Release.Set();
    }

    [Fact]
    public async Task On_a_server_an_off_thread_handler_runs_on_the_pool_so_the_sessions_thread_is_never_held()
    {
        var gate = new Gate();
        var services = new ServiceCollection().AddSingleton(gate).AddRaskCqrs().BuildServiceProvider();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var session = new SessionContext();
        var previous = SynchronizationContext.Current;
        Task<int> call;

        SynchronizationContext.SetSynchronizationContext(session);
        try
        {
            call = dispatcher.Query(new Grind(9), TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.False(call.IsCompleted);
        await gate.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(Environment.CurrentManagedThreadId, gate.Thread);
        Assert.Null(gate.Context);
        gate.Release.Set();
        Assert.Equal(9, await call);
    }

    private static string Header(byte[] frame) =>
        Encoding.UTF8.GetString(frame, sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(frame));

    // What a live session's handler runs under: a context of its own, which the handler must not be held on.
    private sealed class SessionContext : SynchronizationContext;

    private sealed class BrowserApp : IDisposable
    {
        private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(100);

        private readonly ServiceProvider _page;
        private readonly ServiceProvider _worker;

        public BrowserApp(TimeSpan? grace = null)
        {
            OffThreadInvokers.Install();
            _worker = new ServiceCollection().AddSingleton(Gate).AddSingleton<WorkerSide>().AddRaskCqrs().BuildServiceProvider();
            Channel = new InProcessChannel(new OffThreadWorker(_worker));
            _page = new ServiceCollection()
                .AddSingleton(Gate)
                .AddRaskCqrs()
                .AddSingleton(sp => new OffThreadDispatch(sp, Channel, grace ?? ShortGrace))
                .BuildServiceProvider();
        }

        public Gate Gate { get; } = new();

        public InProcessChannel Channel { get; }

        public IDispatcher Page => _page.GetRequiredService<IDispatcher>();

        public void Dispose()
        {
            Gate.Release.Set();
            _page.Dispose();
            _worker.Dispose();
        }
    }

    private sealed class InProcessChannel(OffThreadWorker worker) : IOffThreadChannel
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pending = new();
        private int _calls;
        private int _cancels;
        private int _restarts;

        public int Calls => _calls;

        public int Cancels => _cancels;

        public int Restarts => _restarts;

        public byte[]? LastFrame { get; private set; }

        public TaskCompletionSource Restarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The worker answered a call: what the page's grace waits for before it would restart the worker.
        public TaskCompletionSource Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]> Call(int id, byte[] frame)
        {
            Interlocked.Increment(ref _calls);
            LastFrame = frame;
            var answer = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = answer;
            _ = Task.Run(async () =>
            {
                var reply = await worker.Handle(id, frame);
                if (_pending.TryRemove(id, out var waiting))
                {
                    waiting.TrySetResult(reply);
                }

                Answered.TrySetResult();
            });
            return answer.Task;
        }

        public void Cancel(int id)
        {
            Interlocked.Increment(ref _cancels);
            worker.Cancel(id);
        }

        public void Restart()
        {
            Interlocked.Increment(ref _restarts);
            foreach (var id in _pending.Keys)
            {
                if (_pending.TryRemove(id, out var waiting))
                {
                    waiting.TrySetException(new InvalidOperationException("The worker was restarted."));
                }
            }

            Restarted.TrySetResult();
        }

        public void Warm()
        {
        }
    }
}
