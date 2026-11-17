using Rask.Background;

namespace Rask.Site.Features.UiDocs.Demos;

/// <summary>
///     The rows the app frame demo's bell and list read: the site runs in a browser with no jobs, so this stands in for the
///     Rask.Jobs board — an import that moves on while the page is open and finishes, an export that failed and a report
///     that is done. What a <c>.Noticed(…)</c> job's rows look like to its enqueuer, played out.
/// </summary>
/// <remarks>
///     Its clock starts at the first read, so the first paint — the prerender, the markup a test keeps — is always the same:
///     the import at 1,200 of 5,000 rows.
/// </remarks>
internal sealed class DemoWorkBoard(TimeProvider clock) : IWorkBoard
{
    private const int Rows = 5_000;
    private const int RowsPerSecond = 160;

    private DateTime? _opened;

    /// <inheritdoc />
    public TimeSpan Every => TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var opened = _opened ??= now;
        var kept = Math.Min(Rows, 1_200 + (int)((now - opened).TotalSeconds * RowsPerSecond));
        var import = kept < Rows
            ? new WorkReported(3, "Import of prices.csv", $"{kept:N0} rows kept", kept, Rows, WorkState.Running, opened)
            : new WorkReported(3, "Import of prices.csv", $"{Rows:N0} rows kept", Rows, Rows, WorkState.Done, opened.AddSeconds(24));
        IReadOnlyList<WorkReported> work =
        [
            import,
            new(2, "Export of March invoices", null, 310, 900, WorkState.Failed, opened.AddMinutes(-4)),
            new(1, "Monthly fuel report", "12 pages", null, null, WorkState.Done, opened.AddMinutes(-9)),
        ];
        return Task.FromResult(work);
    }
}
