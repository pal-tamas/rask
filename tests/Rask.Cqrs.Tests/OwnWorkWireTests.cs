using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;

namespace Rask.Cqrs.Tests;

// The work its enqueuer watches, as it crosses to a browser (#1348): the board's read and the watch, their JSON, and the
// browser's board that asks the server.
public sealed class OwnWorkWireTests
{
    private static readonly Guid Alice = Guid.Parse("8f0f6a51-5a3c-4b0e-9b8e-6f6b8c1d2e3f");
    private static readonly Guid Acme = Guid.Parse("1b2c3d4e-0000-4000-8000-00000000a0a0");
    private static readonly DateTime At = new(2026, 11, 17, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_report_crosses_as_what_its_enqueuer_sees_and_never_whose_it_is()
    {
        var work = new WorkReported(Id(7), "Import of prices.csv", "1,200 rows kept", 1200, 5000, WorkState.Running, At)
        {
            Owner = Alice,
            Tenant = Acme,
            Lane = "imports",
        };

        var json = Written(writer => OwnWorkWire.Watch.WriteResult!(writer, work));
        var back = (WorkReported)Read(json, OwnWorkWire.Watch.ReadResult!)!;

        Assert.Equal(work with { Owner = null, Tenant = null, Lane = null }, back);
        Assert.DoesNotContain(Alice.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Acme.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("imports", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_board_s_read_crosses_with_how_often_it_changes_and_a_failure_says_only_failed()
    {
        var read = new OwnWorkRead(TimeSpan.FromSeconds(3),
        [
            new WorkReported(Id(9), "Export of March invoices", null, 310, 900, WorkState.Failed, At),
            new WorkReported(Id(8), "Monthly fuel report", "12 pages", null, null, WorkState.Done, DateTime.SpecifyKind(At, DateTimeKind.Unspecified)),
        ]);

        var json = Written(writer => OwnWorkWire.Rows.WriteResult!(writer, read));
        var back = (OwnWorkRead)Read(json, OwnWorkWire.Rows.ReadResult!)!;

        Assert.Equal(TimeSpan.FromSeconds(3), back.Every);
        Assert.Equal(read.Work, back.Work);
        Assert.All(back.Work, w => Assert.Equal(DateTimeKind.Utc, w.At.Kind));
        Assert.Contains("\"state\":\"Failed\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("imports")]
    public void The_read_and_the_watch_carry_the_lane_they_ask_for_and_nothing_else(string? lane)
    {
        var read = Written(writer => OwnWorkWire.Rows.WriteMessage(writer, new ReadOwnWork(lane), []));
        var watch = Written(writer => OwnWorkWire.Watch.WriteMessage(writer, new WatchOwnWork(lane), []));

        var readBack = Read(read, (ref reader) => OwnWorkWire.Rows.ReadMessage(ref reader, []));
        var watchBack = Read(watch, (ref reader) => OwnWorkWire.Watch.ReadMessage(ref reader, []));

        Assert.Equal(new ReadOwnWork(lane), readBack);
        Assert.Equal(new WatchOwnWork(lane), watchBack);
        Assert.Equal(read, watch);
    }

    [Theory]
    [InlineData("""{"id":1,"title":"x","at":"2026-11-17T09:30:00Z"}""")]
    [InlineData("""{"id":1,"title":"x","state":"Done","at":"2026-11-17T09:30:00Z"}""")]
    [InlineData("""["not","work"]""")]
    public void A_report_that_is_not_one_is_refused_as_malformed(string json)
    {
        var refused = Record.Exception(() => Read(json, OwnWorkWire.Watch.ReadResult!));

        Assert.IsAssignableFrom<JsonException>(refused);
    }

    [Fact]
    public void A_batch_its_detail_and_whether_it_is_shared_or_the_readers_own_cross_and_its_kind_does_not()
    {
        var batch = Guid.Parse("01937f2a-0000-7000-8000-000000000001");
        var work = new WorkReported(Id(7), "Import of b.csv", null, 3, 3, WorkState.Failed, At)
        {
            Batch = batch,
            Detail = "b.csv has no header row",
            Owner = Alice,
            Tenant = Acme,
            Shared = true,
            Mine = false,
            Kind = "Eszaf.Loads.ImportLoad",
        };

        var json = Written(writer => OwnWorkWire.Watch.WriteResult!(writer, work));
        var back = (WorkReported)Read(json, OwnWorkWire.Watch.ReadResult!)!;

        Assert.Equal(batch, back.Batch);
        Assert.Equal("b.csv has no header row", back.Detail);
        Assert.True(back.Shared);
        Assert.False(back.Mine);
        Assert.Null(back.Kind);
        Assert.DoesNotContain("Eszaf.Loads", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_from_before_batches_reads_with_none_of_them()
    {
        const string json = """{"id":"019ac4f0-5a00-7000-8000-000000000001","title":"x","progress":null,"done":null,"of":null,"state":"Done","at":"2026-11-17T09:30:00Z"}""";

        var back = (WorkReported)Read(json, OwnWorkWire.Watch.ReadResult!)!;

        Assert.Equal(WorkState.Done, back.State);
        Assert.Null(back.Batch);
        Assert.Null(back.Detail);
        Assert.False(back.Shared);
        Assert.Null(back.Mine);
    }

    [Fact]
    public void A_warned_job_crosses_as_done_for_a_tab_from_before_warnings_and_as_warned_for_one_after()
    {
        var work = new WorkReported(Id(7), "Import", null, null, null, WorkState.Warned, At) { Detail = "No new rows" };

        var json = Written(writer => OwnWorkWire.Watch.WriteResult!(writer, work));
        var back = (WorkReported)Read(json, OwnWorkWire.Watch.ReadResult!)!;

        Assert.Contains("\"state\":\"Done\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Warned\"", json, StringComparison.Ordinal);
        Assert.Equal(WorkState.Warned, back.State);
    }

    [Theory]
    [InlineData("""{"id":"019ac4f0-5a00-7000-8000-000000000001","title":"x","state":"Exploded","at":"2026-11-17T09:30:00Z"}""")]
    [InlineData("""{"id":"019ac4f0-5a00-7000-8000-000000000001","title":"x","state":"7","at":"2026-11-17T09:30:00Z"}""")]
    [InlineData("""{"id":"019ac4f0-5a00-7000-8000-000000000001","title":"x","state":"Done","outcome":"Paused","at":"2026-11-17T09:30:00Z","colour":"teal"}""")]
    public void A_state_a_newer_server_knows_and_this_reader_does_not_reads_as_an_end_rather_than_breaking_the_bell(string json)
    {
        var back = (WorkReported)Read(json, OwnWorkWire.Watch.ReadResult!)!;

        Assert.Equal(WorkState.Done, back.State);
        Assert.True(back.HasEnded);
    }

    [Fact]
    public void Installed_the_read_is_a_query_for_a_signed_in_caller_and_the_watch_a_subscription()
    {
        OwnWorkWire.Install();

        OwnWorkWire.Install();

        Assert.True(RemoteContractRegistry.TryGet(typeof(ReadOwnWork), out var rows));
        Assert.True(RemoteContractRegistry.TryGet(OwnWorkWire.Watch.Name, out var watch));
        Assert.Equal(RemoteMessageKind.Query, rows!.Kind);
        Assert.True(rows.RequiresAuthentication);
        Assert.False(rows.AllowAnonymous);
        Assert.Null(rows.Invoker);
        Assert.Equal(RemoteMessageKind.Subscription, watch!.Kind);
        Assert.False(watch.SubscribeAnonymously);
    }

    [Fact]
    public async Task The_server_answers_the_read_from_the_board_of_the_scope_that_asks()
    {
        var board = new OneBoard(TimeSpan.FromSeconds(2), new WorkReported(Id(7), "Import", null, 1, 4, WorkState.Running, At));
        using var services = new ServiceCollection()
            .AddSingleton<IWorkBoard>(board)
            .BuildServiceProvider();

        var answer = (OwnWorkRead)(await OwnWorkWire.Rows.LocalInvoker!(services, new ReadOwnWork("imports"), TestContext.Current.CancellationToken))!;

        Assert.Equal(TimeSpan.FromSeconds(2), answer.Every);
        Assert.Equal(board.Work, answer.Work);
        Assert.Equal("imports", board.AskedFor);
    }

    [Fact]
    public async Task A_server_with_no_board_answers_no_work()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var answer = (OwnWorkRead)(await OwnWorkWire.Rows.LocalInvoker!(services, new ReadOwnWork(null), TestContext.Current.CancellationToken))!;

        Assert.Empty(answer.Work);
        Assert.Equal(TimeSpan.Zero, answer.Every);
    }

    [Fact]
    public async Task The_browser_s_board_reads_the_server_s_rows_and_learns_how_often_they_change()
    {
        var work = new WorkReported(Id(7), "Import", "half", 2, 4, WorkState.Running, At);
        var server = new ScriptedServer(_ => new OwnWorkRead(TimeSpan.FromSeconds(30), [work]));
        var board = new RemoteWorkBoard(server);
        var before = board.Every;

        var rows = await board.Mine("imports", TestContext.Current.CancellationToken);

        Assert.Equal(RemoteWorkBoard.Unread, before);
        Assert.Equal([work], rows);
        Assert.Equal(TimeSpan.FromSeconds(30), board.Every);
        Assert.Equal(new ReadOwnWork("imports"), server.Sent);
        Assert.Same(OwnWorkWire.Rows, server.Contract);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task A_refused_or_unknown_read_is_an_empty_board_that_keeps_its_poll(int status)
    {
        var board = new RemoteWorkBoard(new ScriptedServer(_ => throw new RemoteDispatchException("no") { StatusCode = status }));

        var rows = await board.Mine(null, TestContext.Current.CancellationToken);

        Assert.Empty(rows);
        Assert.Equal(RemoteWorkBoard.Unread, board.Every);
    }

    [Fact]
    public async Task A_server_fault_reaches_the_reader_so_the_rows_it_had_stay()
    {
        var board = new RemoteWorkBoard(new ScriptedServer(_ => throw new RemoteDispatchException("boom") { StatusCode = 500 }));

        var fault = await Record.ExceptionAsync(() => board.Mine(null, TestContext.Current.CancellationToken));

        Assert.IsType<RemoteDispatchException>(fault);
    }

    private static string Written(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static object? Read(string json, RemoteResultReader read)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return read(ref reader);
    }

    private sealed class OneBoard(TimeSpan every, params WorkReported[] work) : IWorkBoard
    {
        public IReadOnlyList<WorkReported> Work => work;

        public string? AskedFor { get; private set; }

        public TimeSpan Every => every;

        public Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
        {
            AskedFor = lane;
            return Task.FromResult<IReadOnlyList<WorkReported>>(work);
        }
    }

    // What the client transport would send, answered without a server.
    private sealed class ScriptedServer(Func<object, OwnWorkRead> answer) : IRemoteDispatch
    {
        public object? Sent { get; private set; }

        public RemoteContract? Contract { get; private set; }

        public Task<TResult> Send<TResult>(RemoteContract contract, object message, CancellationToken cancellationToken)
        {
            Contract = contract;
            Sent = message;
            return Task.FromResult((TResult)(object)answer(message));
        }

        public Task Send(RemoteContract contract, object message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task Publish(RemoteContract contract, object e, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // A job's id as a test names it: the nth, a version-7 Guid that reads as its number.
    private static Guid Id(int n) => new($"019ac4f0-5a00-7000-8000-{n:D12}");
}
