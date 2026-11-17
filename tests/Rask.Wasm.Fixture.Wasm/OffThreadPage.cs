using Rask.Core;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Workbooks;

namespace Rask.Wasm.Fixture.Wasm;

/// <summary>
///     A workbook read off the page's thread: <c>#load</c> fetches it from the host, <c>#read</c> reads it in the
///     <c>[OffThread]</c> worker (many times over, so the read outlasts what the journey watches), <c>#cancel</c> stops
///     it, <c>#ping</c> asks the worker something quick.
/// </summary>
[Route("/offthread")]
public sealed partial class OffThreadPage(HttpClient http) : Component
{
    private byte[] _workbook = [];
    private CancellationTokenSource? _reading;
    private string _status = "idle";

    protected override Component? Render() =>
    [
        P.Id("status")[_status],
        Button.Id("load").OnClick(Load)["load"],
        Button.Id("read").OnClick(Read)["read"],
        Button.Id("cancel").OnClick(Cancel)["cancel"],
        Button.Id("ping").OnClick(Ping)["ping"]
    ];

    private async Task Load()
    {
        _workbook = await http.GetByteArrayAsync(new Uri("api/big.xlsx", UriKind.Relative));
        _status = $"loaded {_workbook.Length}";
    }

    // Started, not awaited: a page's handlers take their turns, so a Cancel pressed while this one still waited would
    // wait behind it. The read's own continuation paints its outcome.
    private void Read()
    {
        _reading = new CancellationTokenSource();
        _status = "reading";
        _ = ReadAsync(_reading.Token);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await Dispatcher.Query(new ReadWorkbook(_workbook, 1000), cancellationToken);
            _status = $"read {rows}";
        }
        catch (OperationCanceledException)
        {
            _status = "cancelled";
        }

        StateHasChanged();
    }

    private async Task Cancel()
    {
        if (_reading is { } reading)
        {
            await reading.CancelAsync();
        }
    }

    private async Task Ping() => _status = await Dispatcher.Query(new Ping());
}

/// <summary>Reads the workbook <see cref="Times" /> times over, without once looking at its cancellation.</summary>
[OffThread]
public sealed record ReadWorkbook(byte[] Workbook, int Times) : IQuery<int>;

public sealed class ReadWorkbookHandler : IQueryHandler<ReadWorkbook, int>
{
    public Task<int> Handle(ReadWorkbook query)
    {
        var rows = 0;
        for (var i = 0; i < query.Times; i++)
        {
            rows += Xlsx.Read(query.Workbook).Rows.Count();
        }

        return Task.FromResult(rows);
    }
}

[OffThread]
public sealed record Ping : IQuery<string>;

public sealed class PingHandler : IQueryHandler<Ping, string>
{
    public Task<string> Handle(Ping query) => Task.FromResult("pong");
}
