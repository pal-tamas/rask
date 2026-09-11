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
            return DashboardEmpty.Heading("Storage isn't registered")
                .Detail("Call AddRaskStorage<TContext>() and modelBuilder.AddRaskStorage() to see stored files here.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            UiHeader.Heading("Storage").Caption(
                $"new files go to {_stats.ActiveProvider} · orphans swept every {DashboardParts.Duration(_stats.SweepInterval)}"),
            DashboardError.Message(LoadError),
            DiskNotice(),
            Div.Class("mb-4 sm:mb-5")[
                UiMetricRow.Columns(3)[
                    UiMetric.Key("files").Label("Files").Value(_stats.Files.ToString(CultureInfo.InvariantCulture)),
                    UiMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                    UiMetric
                        .Key("public")
                        .Label("Public")
                        .Value(_stats.Public.ToString(CultureInfo.InvariantCulture))
                        .Caption("served to anyone with the link")
                ]
            ],
            ProviderTable(),
            Div.Class("mb-4")[
                UiSearch
                    .Placeholder("Search file names")
                    .AccessibleLabel("Search stored files")
                    .Value(Search)
                    .OnSearch(SearchAsync)
            ],
            _rows.Count == 0
                ? DashboardEmpty.Heading(Search is { Length: > 0 } ? $"No files matching \"{Search}\"" : "No files stored yet")
                    .Detail("Files appear here as soon as the app saves one.")
                : FileTable(now),
            Pager(),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    // The one state in which nothing backs the files up, which an operator should not have to go and read
    // the docs to find out.
    private Component? DiskNotice() =>
        _stats.ActiveProvider == StorageProvider.Disk || _stats.ByProvider.Any(p => p.Provider == StorageProvider.Disk)
            ? UiNotice.Tone("warn")[
                Span.Class("min-w-0 grow break-words")[
                    "Files on disk are not covered by rask db backup, Litestream or snapshots, and live on this host "
                    + "only. Use S3 or Azure for uploads you can't afford to lose."
                ]
            ]
            : null;

    private Component? ProviderTable() =>
        _stats.ByProvider.Count <= 1
            ? null
            : Div.Class("mb-4 sm:mb-5")[
                UiTable.Scroll(true)[
                    Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                        Tr[
                            Th.Class("px-3 py-2 font-medium")["Provider"],
                            Th.Class("px-3 py-2 font-medium")["Files"],
                            Th.Class("px-3 py-2 font-medium")["Stored"]
                        ]
                    ],
                    Tbody[_stats.ByProvider.Select(p => Tr.Key(p.Provider.ToString()).Class("border-b border-ui-line/60 last:border-0")[
                        Td.Class("px-3 py-2")[p.Provider.ToString()],
                        Td.Class("px-3 py-2 tabular-nums")[p.Files.ToString(CultureInfo.InvariantCulture)],
                        Td.Class("px-3 py-2 tabular-nums")[DashboardParts.Bytes(p.Bytes)]
                    ])]
                ]
            ];

    private Task SearchAsync(string value)
    {
        // Navigate rather than reload, so the address bar carries the search (#936).
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        navigator.NavigateTo(Routes.StoragePage(Search: Search));
        return Task.CompletedTask;
    }

    private Component FileTable(DateTime now) =>
        UiTable.Scroll(true)[
            Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                Tr[
                    Th.Class("px-3 py-2 font-medium")["Name"],
                    Th.Class("hidden px-3 py-2 font-medium md:table-cell")["Type"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Size"],
                    Th.Class("hidden px-3 py-2 font-medium lg:table-cell")["Provider"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Saved"]
                ]
            ],
            Tbody[_rows.Select(r => Tr.Key(r.Id.ToString("N")).Class("border-b border-ui-line/60 last:border-0")[
                Td.Class("w-full max-w-0 px-3 py-2 align-top")[
                    Div.Class("min-w-0")[
                        Div.Class("flex min-w-0 items-center gap-2")[
                            Span.Class("truncate sm:max-w-[28rem]").Title(r.Name)[r.Name],
                            r.Public ? UiBadge["public"] : null
                        ],
                        Div.Class($"mt-0.5 truncate text-xs text-ui-muted {UiStyles.Mono}").Title(r.Id.ToString())[r.Id.ToString("N")],
                        // Size and age follow the name down when their own columns are gone.
                        Div.Class("mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ui-muted sm:hidden")[
                            Span.Class("tabular-nums")[DashboardParts.Bytes(r.Size)],
                            Span[DashboardParts.Ago(r.CreatedAt, now)]
                        ]
                    ]
                ],
                Td.Class($"hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted md:table-cell {UiStyles.Mono}")[r.ContentType],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top tabular-nums sm:table-cell")[DashboardParts.Bytes(r.Size)],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted lg:table-cell")[r.Provider.ToString()],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted sm:table-cell")
                    .Title(r.CreatedAt.ToString("u", CultureInfo.InvariantCulture))[DashboardParts.Ago(r.CreatedAt, now)]
            ])]
        ];

    private Component? Pager()
    {
        var pages = (int)Math.Ceiling(_total / (double)options.PageSize);
        if (pages <= 1)
        {
            return null;
        }

        return Div.Class("mt-4 flex items-center justify-between gap-3")[
            UiButton.Key("prev")
                .Disabled(_page == 0)
                .OnClick(() => GoAsync(_page - 1))["Previous"],
            Span.Class("text-center text-xs text-ui-muted")[
                Span[$"Page {_page + 1} of {pages}"],
                Span.Class("hidden sm:inline")[$" — {_total} files"]
            ],
            UiButton.Key("next")
                .Disabled(_page >= pages - 1)
                .OnClick(() => GoAsync(_page + 1))["Next"]
        ];
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
