using System.Globalization;
using Rask.Core;
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
    OpsOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private StorageStats _stats = StorageStats.Empty;
    private IReadOnlyList<StoredFileRow> _rows = [];
    private int _total;
    private int _page;

    /// <summary>Substring filter on the file name, from the query string so a search is a shareable link.</summary>
    [QueryParam("q")]
    public string? Search { get; set; }

    /// <inheritdoc />
    protected override OpsOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> Load(CancellationToken cancellationToken)
    {
        if (!storage.IsAvailable)
        {
            return null;
        }

        _stats = await storage.Stats(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await storage
            .Page(Search, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        if (_rows.Count == 0 && _page > DashboardParts.LastPageIndex(_total, options.PageSize))
        {
            _page = DashboardParts.LastPageIndex(_total, options.PageSize);
            (_rows, _total) = await storage
                .Page(Search, _page * options.PageSize, options.PageSize, cancellationToken)
                .ConfigureAwait(false);
        }

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
            return Ui.Card[
                Ui.Empty
                    .Title("Storage isn't registered")
                    .Detail("Call AddRaskStorage<TContext>() and modelBuilder.AddRaskStorage() to see stored files here.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            Ui.Header.Title("Storage").Caption(
                $"new files go to {_stats.ActiveProvider} · orphans swept every {DashboardParts.Duration(_stats.SweepInterval)}"),
            DashboardError.Message(LoadError),
            DiskNotice(),
            Ui.MetricRow.Columns(3)[
                Ui.Metric.Key("files").Label("Files").Value(_stats.Files.ToString(CultureInfo.InvariantCulture)),
                Ui.Metric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                Ui.Metric
                    .Key("public")
                    .Label("Public")
                    .Value(_stats.Public.ToString(CultureInfo.InvariantCulture))
                    .Caption("served to anyone with the link")
            ],
            ProviderGrid(),
            FileGrid(now),
            DashboardParked.Parked(IsParked).Resume(Resume),
        ];
    }

    // The one state in which only a manual backup covers the files, which an operator should not have to go and
    // read the docs to find out.
    private UiCallout? DiskNotice() =>
        _stats.ActiveProvider == StorageProvider.Disk || _stats.ByProvider.Any(p => p.Provider == StorageProvider.Disk)
            ? Ui.Callout.Warning.Icon(Ui.IconName.ExclamationTriangle)
                .Heading("Files on disk live on this host only.")
                .Text("rask db backup archives them beside the database, but Litestream and snapshots copy the database alone. "
                    + "Use S3 or Azure for uploads you can't afford to lose.")
            : null;

    // Only once files are split across providers: a single row would restate the tiles above it.
    private Component? ProviderGrid() =>
        _stats.ByProvider.Count <= 1
            ? null
            : Ui.DataGrid.Data(_stats.ByProvider).RowKey(p => p.Provider).Label("Stored files by provider")[c => [
                c.Field(p => p.Provider).Title("Provider"),
                c.Field(p => p.Files).Title("Files").Value(p => p.Files.ToString(CultureInfo.InvariantCulture)),
                c.Field(p => p.Bytes).Title("Stored").Value(p => DashboardParts.Bytes(p.Bytes)),
            ]];

    private Task SearchAsync(string value)
    {
        // Navigate rather than reload, so the address bar carries the search (#936).
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        Go.To(Routes.StoragePage(Search: Search));
        return Task.CompletedTask;
    }

    // The name is the column an operator came for, so it is the one every width keeps. The type and the provider
    // wait until the table has room; the id shows at every width, as it always did under the name, and a phone
    // lists every column as its own line.
    private Component FileGrid(DateTime now) =>
        Ui.DataGrid.Data(_rows)
            .RowKey(r => r.Id)
            .Label("Stored files, newest first")
            .PageSize(options.PageSize)
            .Page(_page)
            .TotalCount(_total)
            .OnPage(GoAsync)
            .Toolbar(Ui.Input
                .Value(Search ?? string.Empty)
                .Type(InputType.Search)
                .Icon(Ui.IconName.MagnifyingGlass)
                .Placeholder("Search file names")
                .Attributes(("aria-label", "Search file names"))
                .OnChange(SearchAsync))
            .Empty(Ui.Empty
                .Title(Search is { Length: > 0 } ? $"No files matching \"{Search}\"" : "No files stored yet")
                .Detail("Files appear here as soon as the app saves one."))[c => [
                c.Field(r => r.Name).Title("Name"),
                c.Field(r => r.Public).Title("Access").Value(r => r.Public ? "public" : "private"),
                c.Field(r => r.ContentType).Title("Type").Mono().ShowFrom(Ui.Breakpoint.Md),
                c.Field(r => r.Size).Title("Size").Value(r => DashboardParts.Bytes(r.Size)),
                c.Field(r => r.Provider).Title("Provider").ShowFrom(Ui.Breakpoint.Lg),
                c.Field(r => r.Id).Title("Id").Mono().Value(r => r.Id.ToString("N")),
                c.Field(r => r.CreatedAt).Title("Saved").Cell(r =>
                    Span.Title(r.CreatedAt.ToString("u", CultureInfo.InvariantCulture))[DashboardParts.Ago(r.CreatedAt, now)]),
            ]];

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
