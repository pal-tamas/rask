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
                DashboardEmpty
                    .Title("Storage isn't registered")
                    .Detail("Call AddRaskStorage<TContext>() and modelBuilder.AddRaskStorage() to see stored files here.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            DashboardHeading.Title("Storage").Caption(
                $"new files go to {_stats.ActiveProvider} · orphans swept every {DashboardParts.Duration(_stats.SweepInterval)}"),
            DashboardError.Message(LoadError),
            DiskNotice(),
            Div.Class("grid gap-4 sm:grid-cols-3")[
                DashboardMetric.Key("files").Label("Files").Value(_stats.Files.ToString(CultureInfo.InvariantCulture)),
                DashboardMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                DashboardMetric
                    .Key("public")
                    .Label("Public")
                    .Value(_stats.Public.ToString(CultureInfo.InvariantCulture))
                    .Caption("served to anyone with the link")
            ],
            ProviderTable(),
            DashboardSearch.Placeholder("Search file names").Value(Search).OnChange(SearchAsync),
            FileTable(now),
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

    // Only once files are split across providers: a single row would restate the figures above it.
    private Component? ProviderTable() =>
        _stats.ByProvider.Count <= 1
            ? null
            : Ui.Table.Class("w-full").AriaLabel("Stored files by provider")[
                Ui.TableColumns[
                    Ui.TableColumn["Provider"],
                    Ui.TableColumn.Class("w-32")["Files"],
                    Ui.TableColumn.Class("w-32")["Stored"]
                ],
                Ui.TableRows[
                    _stats.ByProvider.Select(p => Ui.TableRow.Key(p.Provider)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[p.Provider.ToString()],
                        Ui.TableCell[p.Files.ToString(CultureInfo.InvariantCulture)],
                        Ui.TableCell[DashboardParts.Bytes(p.Bytes)]
                    ])
                ]
            ];

    private Task SearchAsync(string value)
    {
        // Navigate rather than reload, so the address bar carries the search (#936).
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        Go.To(Routes.StoragePage(Search: Search));
        return Task.CompletedTask;
    }

    // The name is the column an operator came for, so it is the one without a width: it takes what is left, and a
    // long name ends in an ellipsis with the whole of it in its tooltip. On a phone the table scrolls in its box.
    private Component FileTable(DateTime now)
    {
        if (_rows.Count == 0)
        {
            return Ui.Card[
                DashboardEmpty
                    .Title(Search is { Length: > 0 } ? $"No files matching \"{Search}\"" : "No files stored yet")
                    .Detail("Files appear here as soon as the app saves one.")
            ];
        }

        return Ui.Table
            .Class("w-full min-w-240")
            .AriaLabel("Stored files, newest first")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _page + 1, PerPage = options.PageSize, Total = _total })
                .OnPage(page => GoAsync(page - 1)))[
            Ui.TableColumns[
                Ui.TableColumn["Name"],
                Ui.TableColumn.Class("w-24")["Access"],
                Ui.TableColumn.Class("w-40")["Type"],
                Ui.TableColumn.Class("w-24")["Size"],
                Ui.TableColumn.Class("w-24")["Provider"],
                Ui.TableColumn.Class("w-48")["Id"],
                Ui.TableColumn.Class("w-28")["Saved"]
            ],
            Ui.TableRows[
                _rows.Select(r => Ui.TableRow.Key(r.Id)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate").Title(r.Name)[r.Name],
                    Ui.TableCell[r.Public ? "public" : "private"],
                    Ui.TableCell.Class("truncate font-mono").Title(r.ContentType)[r.ContentType],
                    Ui.TableCell[DashboardParts.Bytes(r.Size)],
                    Ui.TableCell[r.Provider.ToString()],
                    Ui.TableCell.Class("truncate font-mono").Title(r.Id.ToString("N"))[r.Id.ToString("N")],
                    Ui.TableCell[
                        Span.Title(r.CreatedAt.ToString("u", CultureInfo.InvariantCulture))[DashboardParts.Ago(r.CreatedAt, now)]
                    ]
                ])
            ]
        ];
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
