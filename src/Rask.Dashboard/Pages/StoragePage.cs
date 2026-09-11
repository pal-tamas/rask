using System.Globalization;
using Rask.Core.Routing;
using Rask.Dashboard.Panels;
using Rask.Storage;

namespace Rask.Dashboard.Pages;

/// <summary>
/// What is stored: how many files, how many bytes, where they are, and the newest of them. Read-only — a file
/// deleted from here would leave the entity that refers to it pointing at nothing.
/// </summary>
[Route("storage")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class StoragePage(
    IStoragePanelReader storage,
    RaskDashboardOptions options,
    TimeProvider timeProvider,
    Navigator navigator) : PollingPanel
{
    private StorageStats _stats = StorageStats.Empty;
    private IReadOnlyList<StoredFileRow> _rows = [];
    private int _total;
    private int _page;

    /// <summary>Substring filter on the file name, from the query string so a search is a shareable link.</summary>
    [QueryParam("q")]
    public string? Search { get; set; }

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!storage.IsAvailable)
        {
            return null;
        }

        _stats = await storage.StatsAsync(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await storage
            .PageAsync(Search, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        return string.Join('|',
            [$"{_stats.Files}:{_stats.Bytes}:{_stats.Public}:{_total}",
             .. _rows.Select(r => r.Id.ToString("N"))]);
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        if (!storage.IsAvailable)
        {
            return UiCard[
                UiEmpty
                    .Heading("Storage isn't registered")
                    .Detail("Call AddRaskStorage<TContext>() and modelBuilder.AddRaskStorage() to see stored files here.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            UiHeader.Heading("Storage").Caption(
                $"new files go to {_stats.ActiveProvider} · orphans swept every {DashboardParts.Duration(_stats.SweepInterval)}"),
            DashboardError.Message(LoadError),
            DiskNotice(),
            UiMetricRow.Columns(3)[
                UiMetric.Key("files").Label("Files").Value(_stats.Files.ToString(CultureInfo.InvariantCulture)),
                UiMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                UiMetric
                    .Key("public")
                    .Label("Public")
                    .Value(_stats.Public.ToString(CultureInfo.InvariantCulture))
                    .Caption("served to anyone with the link")
            ],
            ProviderGrid(),
            FileGrid(now),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    // The one state in which nothing backs the files up, which an operator should not have to go and read
    // the docs to find out.
    private Component? DiskNotice() =>
        _stats.ActiveProvider == StorageProvider.Disk || _stats.ByProvider.Any(p => p.Provider == StorageProvider.Disk)
            ? UiAlert.Tone(UiTone.Warning)[
                UiIcon.Name(UiIconName.Warning),
                Span[
                    "Files on disk are not covered by rask db backup, Litestream or snapshots, and live on this host "
                    + "only. Use S3 or Azure for uploads you can't afford to lose."
                ]
            ]
            : null;

    // Only once files are split across providers: a single row would restate the tiles above it.
    private Component? ProviderGrid() =>
        _stats.ByProvider.Count <= 1
            ? null
            : UiDataGrid.Data(_stats.ByProvider).RowKey(p => p.Provider).Label("Stored files by provider")[c => [
                c.Field(p => p.Provider).Title("Provider"),
                c.Field(p => p.Files).Title("Files").Value(p => p.Files.ToString(CultureInfo.InvariantCulture)),
                c.Field(p => p.Bytes).Title("Stored").Value(p => DashboardParts.Bytes(p.Bytes)),
            ]];

    private Task SearchAsync(string value)
    {
        // Navigate rather than reload, so the address bar carries the search (#936).
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        navigator.NavigateTo(Routes.StoragePage(Search: Search));
        return Task.CompletedTask;
    }

    // The name is the column an operator came for, so it is the one every width keeps; the type, the provider
    // and the id wait until the table has room for them, and a phone lists every one as its own line.
    private Component FileGrid(DateTime now) =>
        UiDataGrid.Data(_rows)
            .RowKey(r => r.Id)
            .Label("Stored files, newest first")
            .PageSize(options.PageSize)
            .Page(_page)
            .TotalCount(_total)
            .OnPageChange(GoAsync)
            .Toolbar(UiSearch
                .Placeholder("Search file names")
                .AccessibleLabel("Search stored files")
                .Value(Search)
                .OnSearch(SearchAsync))
            .Empty(UiEmpty
                .Heading(Search is { Length: > 0 } ? $"No files matching \"{Search}\"" : "No files stored yet")
                .Detail("Files appear here as soon as the app saves one."))[c => [
                c.Field(r => r.Name).Title("Name"),
                c.Field(r => r.Public).Title("Access").Value(r => r.Public ? "public" : "private"),
                c.Field(r => r.ContentType).Title("Type").Mono(true).ShowFrom(UiBreakpoint.Md),
                c.Field(r => r.Size).Title("Size").Value(r => DashboardParts.Bytes(r.Size)),
                c.Field(r => r.Provider).Title("Provider").ShowFrom(UiBreakpoint.Lg),
                c.Field(r => r.Id).Title("Id").Mono(true).ShowFrom(UiBreakpoint.Xl).Value(r => r.Id.ToString("N")),
                c.Field(r => r.CreatedAt).Title("Saved").Cell(r =>
                    Span.Title(r.CreatedAt.ToString("u", CultureInfo.InvariantCulture))[DashboardParts.Ago(r.CreatedAt, now)]),
            ]];

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
