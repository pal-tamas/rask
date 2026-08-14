using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.Core.Routing;
using Rask.Example.Shop.Features.Products;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Ops;

/// <summary>
/// A live dashboard over every pillar's own table — the page that makes "it's all on one database" visible.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a plain query against the app's single SQLite file. The outbox, jobs, mail and cache
/// tables are ordinary tables next to <c>Products</c> and <c>Orders</c>, which is the whole argument for
/// DB-backed pillars: no broker to inspect, no second dashboard, just <c>SELECT count(*)</c>.
/// </para>
/// <para>
/// It refreshes itself on a timer rather than behind a button, so the background processors can be watched
/// working. The loop is <b>bounded</b> — every open tab is a reader competing with five writers for the
/// single write lock, so an unbounded 1 Hz poll per session is a real cost, not a free convenience.
/// </para>
/// </remarks>
[Route("ops")]
public sealed partial class OpsPage(IDbContextFactory<AppDbContext> factory, IJobQueue jobs, PopularProducts popular, IConfiguration config)
    : Component, IDisposable
{
    private const int MaxTicks = 120; // ~4 minutes of watching, then it stops on its own.

    private readonly CancellationTokenSource _stopped = new();
    private Snapshot _stats = new();
    private string? _cacheValue;
    private string? _cacheSource;
    private string? _message;

    protected override Component? HeadAssets => [Title["Ops — Rask.Example.Shop"]];

    protected override async Task OnMountAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
        _ = PollAsync();
    }

    // ConfigureAwait(false) throughout: staying off the lifecycle sync-context keeps the loop from
    // triggering a render per await. StateHasChanged is called once per real change instead.
    private async Task PollAsync()
    {
        for (var tick = 0; tick < MaxTicks && !_stopped.IsCancellationRequested; tick++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _stopped.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var before = _stats;
            await RefreshAsync().ConfigureAwait(false);
            if (_stats != before)
            {
                StateHasChanged();
            }
        }
    }

    private async Task RefreshAsync()
    {
        await using var db = await factory.CreateDbContextAsync(_stopped.Token).ConfigureAwait(false);

        _stats = new Snapshot
        {
            Products = await db.Products.CountAsync(_stopped.Token).ConfigureAwait(false),
            Orders = await db.Orders.CountAsync(_stopped.Token).ConfigureAwait(false),
            OutboxTotal = await db.Set<OutboxMessage>().CountAsync(_stopped.Token).ConfigureAwait(false),
            OutboxProcessed = await db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null, _stopped.Token).ConfigureAwait(false),
            OutboxFailed = await db.Set<OutboxMessage>().CountAsync(m => m.Error != null, _stopped.Token).ConfigureAwait(false),
            JobsTotal = await db.Set<Job>().CountAsync(_stopped.Token).ConfigureAwait(false),
            JobsProcessed = await db.Set<Job>().CountAsync(j => j.ProcessedAt != null, _stopped.Token).ConfigureAwait(false),
            MailTotal = await db.Set<QueuedMail>().CountAsync(_stopped.Token).ConfigureAwait(false),
            MailSent = await db.Set<QueuedMail>().CountAsync(m => m.ProcessedAt != null, _stopped.Token).ConfigureAwait(false),
            CacheEntries = await db.Set<CacheEntry>().CountAsync(_stopped.Token).ConfigureAwait(false),
            Snapshots = CountSnapshots(),
            JournalMode = await ScalarAsync(db, "PRAGMA journal_mode").ConfigureAwait(false),
            ForeignKeys = await ScalarAsync(db, "PRAGMA foreign_keys").ConfigureAwait(false),
        };
    }

    private int CountSnapshots()
    {
        var directory = config["Sqlite:SnapshotDirectory"] ?? "snapshots";
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.db").Length : 0;
    }

    private static async Task<string> ScalarAsync(AppDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await db.Database.OpenConnectionAsync().ConfigureAwait(false);
        try
        {
            return (await command.ExecuteScalarAsync().ConfigureAwait(false))?.ToString() ?? "";
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task EnqueueJobAsync()
    {
        await jobs.EnqueueAsync(new Orders.PurgeStaleCarts()).ConfigureAwait(false);
        _message = "Enqueued PurgeStaleCarts — watch Jobs processed climb.";
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task LoadCachedAsync()
    {
        // Two loads in a row show the pillar doing its job: the first computes and stores, the second
        // returns the same value without recomputing.
        var before = _stats.CacheEntries;
        _cacheValue = await popular.GetAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        _cacheSource = _stats.CacheEntries > before ? "Computed fresh" : "Served from cache";
    }

    private async Task ClearCacheAsync()
    {
        await popular.InvalidateAsync().ConfigureAwait(false);
        _cacheValue = null;
        _cacheSource = null;
        await RefreshAsync().ConfigureAwait(false);
    }

    protected override Component? Render() =>
        Div.Class("container py-4")[
            H1["Ops"],
            P.Class("text-muted")[
                "Every pillar below keeps its state in the same SQLite file as Products and Orders. "
                + "This page polls it once a second."
            ],
            _message is null ? null : Div.Id("ops-message").Class("alert alert-info")[_message],

            Div.Class("row g-3")[
                Stat("ops-products", "Products", _stats.Products.ToString()),
                Stat("ops-orders", "Orders", _stats.Orders.ToString()),
                Stat("ops-outbox-processed", "Outbox processed", $"{_stats.OutboxProcessed}/{_stats.OutboxTotal}"),
                Stat("ops-outbox-failed", "Outbox failed", _stats.OutboxFailed.ToString()),
                Stat("ops-jobs-processed", "Jobs processed", $"{_stats.JobsProcessed}/{_stats.JobsTotal}"),
                Stat("ops-mail-sent", "Mail sent", $"{_stats.MailSent}/{_stats.MailTotal}"),
                Stat("ops-cache-entries", "Cache entries", _stats.CacheEntries.ToString()),
                Stat("ops-snapshots", "Snapshots on disk", _stats.Snapshots.ToString())
            ],

            H2.Class("mt-4 h5")["SQLite pragmas"],
            P[
                "journal_mode = ", Code.Id("ops-journal-mode")[_stats.JournalMode],
                " · foreign_keys = ", Code.Id("ops-foreign-keys")[_stats.ForeignKeys]
            ],

            H2.Class("mt-4 h5")["Try the pillars"],
            Div.Class("d-flex gap-2 flex-wrap")[
                BsButton.Id("ops-enqueue-job").OnClickAsync(EnqueueJobAsync)["Enqueue a job"],
                BsButton.Id("ops-cache-load").OnClickAsync(LoadCachedAsync)["Load cached value"],
                BsButton.Id("ops-cache-clear").OnClickAsync(ClearCacheAsync)["Clear cache"]
            ],
            _cacheSource is null
                ? null
                : P.Class("mt-2")[
                    Span.Id("ops-cache-source")[_cacheSource], " — ", Span.Id("ops-cache-value")[_cacheValue ?? ""]
                ]
        ];

    private static Component Stat(string id, string label, string value) =>
        Div.Class("col-6 col-md-3")[
            Div.Class("border rounded p-3")[
                Div.Class("text-muted small")[label],
                Div.Id(id).Class("fs-4")[value]
            ]
        ];

    public void Dispose()
    {
        _stopped.Cancel();
        _stopped.Dispose();
    }

    // A value record so the poll loop can compare snapshots and only re-render on a real change.
    private readonly record struct Snapshot
    {
        public int Products { get; init; }
        public int Orders { get; init; }
        public int OutboxTotal { get; init; }
        public int OutboxProcessed { get; init; }
        public int OutboxFailed { get; init; }
        public int JobsTotal { get; init; }
        public int JobsProcessed { get; init; }
        public int MailTotal { get; init; }
        public int MailSent { get; init; }
        public int CacheEntries { get; init; }
        public int Snapshots { get; init; }
        public string JournalMode { get; init; }
        public string ForeignKeys { get; init; }
    }
}
