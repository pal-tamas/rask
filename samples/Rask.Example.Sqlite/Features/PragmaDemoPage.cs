using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Data;
using Rask.Example.Sqlite.Data;
using Rask.SQLite;

namespace Rask.Example.Sqlite.Features;

// The whole sample on one page: show the live pragma values the connection is actually running with,
// then let the visitor fire a burst of concurrent writers and watch every one commit — the payoff of
// WAL + busy_timeout that a stock `UseSqlite` would turn into "database is locked". The second demo fires
// the same burst through the raw factory's BEGIN IMMEDIATE + non-blocking fair-interval retry.
public sealed partial class PragmaDemoPage(
    IDbContextFactory<DemoDbContext> dbContextFactory,
    IRaskSqliteConnectionFactory connectionFactory) : Page
{
    protected override string Route => "/";

    private const int Workers = 25;

    private const int ImportRows = 10_000;

    // The wiring this sample is about — shown above the live result (code-above-result).
    private const string WiringSnippet =
        """
        builder.Services.AddDbContextFactory<DemoDbContext>(options =>
            options.UseRaskSqlite($"Data Source={dbPath}"));
        // → WAL, synchronous=NORMAL, foreign_keys=ON, busy_timeout=5000,
        //   cache_size, mmap_size, journal_size_limit — applied on every open.
        """;

    // The non-blocking write path: BEGIN IMMEDIATE + a constant 1 ms fair-interval retry that yields the
    // thread while it waits for the write lock (a fair-interval busy handler).
    private const string ImmediateSnippet =
        """
        await connectionFactory.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO WriteLogs (Note) VALUES ($note);";
            cmd.Parameters.AddWithValue("$note", note);
            await cmd.ExecuteNonQueryAsync(ct);
        });
        // BEGIN IMMEDIATE takes the write lock up front; a contended lock is polled every 1 ms
        // (thread-free) until it frees — no "database is locked", no blocked thread.
        """;

    // The bulk import this sample's third card is about — shown above its live result.
    private const string BulkSnippet =
        """
        // Batched through the change tracker: the interceptors still stamp and publish per row.
        await db.BulkInsertAsync(readings);

        // Or straight to the provider — one prepared INSERT, no entity entries, no interceptors.
        await db.BulkInsertAsync(readings, o => o.SkipChangeTracking = true);
        """;

    private IReadOnlyList<(string Name, string Value)> _pragmas = [];
    private int _rowCount;
    private bool _loaded;
    private int _attempted;
    private int _succeeded;
    private bool _hasRun;
    private int _immediateAttempted;
    private int _immediateSucceeded;
    private bool _immediateHasRun;
    private long _trackedMs;
    private long _rawMs;
    private int _readingCount;

    protected override Component? HeadAssets => Title["SQLite pragmas — Rask"];

    protected override async Task OnMountAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
        await db.Database.OpenConnectionAsync(CancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();

        _pragmas =
        [
            ("journal_mode", ReadPragma(connection, "journal_mode")),
            ("synchronous", ReadPragma(connection, "synchronous")),
            ("foreign_keys", ReadPragma(connection, "foreign_keys")),
            ("busy_timeout", ReadPragma(connection, "busy_timeout")),
            ("cache_size", ReadPragma(connection, "cache_size")),
            ("mmap_size", ReadPragma(connection, "mmap_size")),
            ("journal_size_limit", ReadPragma(connection, "journal_size_limit")),
        ];
        _rowCount = await db.WriteLogs.CountAsync(CancellationToken);
        _readingCount = await db.Readings.CountAsync(CancellationToken);

        await db.Database.CloseConnectionAsync();
        _loaded = true;
    }

    // Fire N writers at the same database concurrently, each on its own short-lived context. With the
    // pragmas on, the busy_timeout absorbs the brief write-lock contention and every writer commits.
    private async Task RunWritersAsync()
    {
        var tasks = Enumerable.Range(1, Workers).Select(async n =>
        {
            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                db.WriteLogs.Add(new WriteLog { Note = $"worker {n}" });
                await db.SaveChangesAsync(CancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // EF Core wraps a SQLITE_BUSY from SaveChanges in DbUpdateException. Without WAL +
                // busy_timeout this is where "database is locked" would land.
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        _attempted = Workers;
        _succeeded = results.Count(succeeded => succeeded);
        _hasRun = true;

        await LoadAsync();
    }

    // Fire N writers through the raw factory's non-blocking BEGIN IMMEDIATE path. Each takes the write
    // lock via a 1 ms fair-interval retry that yields the thread while it waits, so all N commit without
    // any thread being blocked on the lock.
    private async Task RunImmediateWritersAsync()
    {
        var tasks = Enumerable.Range(1, Workers).Select(async n =>
        {
            try
            {
                await connectionFactory.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO WriteLogs (Note) VALUES ($note);";
                    command.Parameters.AddWithValue("$note", $"immediate worker {n}");
                    await command.ExecuteNonQueryAsync(ct);
                }, CancellationToken);
                return true;
            }
            catch (SqliteException)
            {
                // With the fair-interval retry this should not happen within the timeout — but if the lock
                // never frees, ExecuteInImmediateTransactionAsync surfaces SQLITE_BUSY here.
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        _immediateAttempted = Workers;
        _immediateSucceeded = results.Count(succeeded => succeeded);
        _immediateHasRun = true;

        await LoadAsync();
    }

    // Import the same rows both ways and time each. The tracked path materialises an entry per row and
    // runs every interceptor; the raw path binds one prepared INSERT per row and runs none — which is what
    // the elapsed times are there to make concrete.
    private async Task ImportAsync(bool skipChangeTracking)
    {
        var readings = Enumerable.Range(0, ImportRows)
            .Select(i => Reading.Create($"sensor-{i % 16}", i * 0.5))
            .ToArray();

        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await db.BulkInsertAsync(readings, o => o.SkipChangeTracking = skipChangeTracking, CancellationToken);
        stopwatch.Stop();

        if (skipChangeTracking)
        {
            _rawMs = stopwatch.ElapsedMilliseconds;
        }
        else
        {
            _trackedMs = stopwatch.ElapsedMilliseconds;
        }

        await LoadAsync();
    }

    private static string ReadPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    protected override Component? Render() =>
    [
        Div.Class("mb-4")[
            H1.Class("h3 mb-1")["SQLite production pragmas"],
            P.Class("text-secondary mb-0")[
                "One line — ", Code["UseRaskSqlite"],
                " — puts the production pragma set (WAL, foreign_keys, busy_timeout, …) on every connection."
            ]
        ],

        // Code above, live result below.
        Div.Class("card shadow-sm mb-4")[
            Div.Class("card-header bg-dark text-light py-2")[
                I.Class("bi bi-code-slash me-2"), "Program.cs"
            ],
            Pre.Class("mb-0 p-3 bg-dark text-light rounded-bottom overflow-auto")[
                Code[WiringSnippet]
            ]
        ],

        !_loaded
            ? Div.Class("text-secondary")[Span.Class("spinner-border spinner-border-sm me-2"), "Loading…"]
            : Div[
                Div.Class("card shadow-sm mb-4")[
                    Div.Class("card-header py-2 fw-semibold")[
                        I.Class("bi bi-sliders me-2"), "Live pragma values on this connection"
                    ],
                    Table.Class("table table-striped align-middle mb-0")[
                        Thead[Tr[Th["PRAGMA"], Th.Class("text-end")["Value"]]],
                        Tbody[
                            _pragmas.Select(p => Tr.Key(p.Name)[
                                Td.Class("fw-semibold font-monospace")[p.Name],
                                Td.Class("text-end font-monospace")[p.Value]
                            ])
                        ]
                    ]
                ],

                Div.Class("card shadow-sm")[
                    Div.Class("card-body")[
                        H2.Class("h5")["Concurrent writers"],
                        P.Class("text-secondary")[
                            $"Fire {Workers.ToString(CultureInfo.InvariantCulture)} writers at the database at once. ",
                            "WAL lets readers and the writer coexist, and the 5-second ",
                            Code["busy_timeout"],
                            " absorbs the momentary write-lock contention — so every writer commits."
                        ],
                        Button.Type("button").Class("btn btn-primary").OnClickAsync(RunWritersAsync)[
                            I.Class("bi bi-lightning-charge me-1"),
                            $"Run {Workers.ToString(CultureInfo.InvariantCulture)} concurrent writers"
                        ],
                        !_hasRun
                            ? Div.Class("text-secondary mt-3 mb-0")[
                                $"Total rows written so far: {_rowCount.ToString(CultureInfo.InvariantCulture)}."
                            ]
                            : Div
                                .Class($"alert mt-3 mb-0 {(_succeeded == _attempted ? "alert-success" : "alert-danger")}")[
                                I
                                    .Class($"bi me-2 {(_succeeded == _attempted ? "bi-check-circle" : "bi-exclamation-triangle")}"),
                                $"{_succeeded.ToString(CultureInfo.InvariantCulture)} of {_attempted.ToString(CultureInfo.InvariantCulture)} writers committed. ",
                                $"Total rows now: {_rowCount.ToString(CultureInfo.InvariantCulture)}."
                            ]
                    ]
                ],

                // Second demo: the non-blocking BEGIN IMMEDIATE + fair-interval retry write path (raw factory).
                Div.Class("card shadow-sm mb-4 mt-4")[
                    Div.Class("card-header bg-dark text-light py-2")[
                        I.Class("bi bi-code-slash me-2"), "Non-blocking IMMEDIATE write"
                    ],
                    Pre.Class("mb-0 p-3 bg-dark text-light rounded-bottom overflow-auto")[
                        Code[ImmediateSnippet]
                    ]
                ],

                // Third demo: loading many rows at once — the bulk insert EF Core leaves out.
                Div.Class("card shadow-sm mb-4 mt-4")[
                    Div.Class("card-header bg-dark text-light py-2")[
                        I.Class("bi bi-code-slash me-2"), "Bulk import"
                    ],
                    Pre.Class("mb-0 p-3 bg-dark text-light rounded-bottom overflow-auto")[
                        Code[BulkSnippet]
                    ]
                ],

                Div.Class("card shadow-sm")[
                    Div.Class("card-body")[
                        H2.Class("h5")["Bulk import"],
                        P.Class("text-secondary")[
                            "EF Core has ", Code["ExecuteUpdate"], " and ", Code["ExecuteDelete"],
                            " but no bulk insert, so ", Code["BulkInsertAsync"], " is Rask's. Import ",
                            ImportRows.ToString("N0", CultureInfo.InvariantCulture),
                            " readings each way and compare: the batched path keeps every interceptor running, ",
                            "while ", Code["SkipChangeTracking"],
                            " writes them with one prepared INSERT and no entity entries at all."
                        ],
                        Div.Class("d-flex gap-2 flex-wrap")[
                            Button.Type("button").Id("import-tracked").Class("btn btn-outline-primary")
                                .OnClickAsync(() => ImportAsync(skipChangeTracking: false))[
                                I.Class("bi bi-database-add me-1"), "Import through the change tracker"
                            ],
                            Button.Type("button").Id("import-raw").Class("btn btn-primary")
                                .OnClickAsync(() => ImportAsync(skipChangeTracking: true))[
                                I.Class("bi bi-lightning-charge me-1"), "Import with SkipChangeTracking"
                            ]
                        ],
                        Div.Id("import-result").Class("mt-3 mb-0 text-secondary")[
                            _trackedMs == 0 && _rawMs == 0
                                ? $"Rows imported so far: {_readingCount.ToString("N0", CultureInfo.InvariantCulture)}."
                                : (Component)Div[
                                    _trackedMs == 0
                                        ? null
                                        : (Component)Div[
                                            "Change tracker: ",
                                            Strong[$"{_trackedMs.ToString("N0", CultureInfo.InvariantCulture)} ms"]
                                        ],
                                    _rawMs == 0
                                        ? null
                                        : (Component)Div[
                                            "SkipChangeTracking: ",
                                            Strong[$"{_rawMs.ToString("N0", CultureInfo.InvariantCulture)} ms"]
                                        ],
                                    Div.Class("mt-1")[
                                        $"Rows imported so far: {_readingCount.ToString("N0", CultureInfo.InvariantCulture)}."
                                    ]
                                ]
                        ]
                    ]
                ],

                Div.Class("card shadow-sm")[
                    Div.Class("card-body")[
                        H2.Class("h5")["Concurrent IMMEDIATE writers (non-blocking)"],
                        P.Class("text-secondary")[
                            $"Fire {Workers.ToString(CultureInfo.InvariantCulture)} writers through ",
                            Code["ExecuteInImmediateTransactionAsync"],
                            ". Each takes the write lock with ", Code["BEGIN IMMEDIATE"],
                            " and, when it's contended, polls every 1 ms — yielding the thread while it waits, ",
                            "a fair-interval busy handler — so every writer commits with no thread blocked."
                        ],
                        Button.Type("button").Class("btn btn-primary").OnClickAsync(RunImmediateWritersAsync)[
                            I.Class("bi bi-lightning-charge me-1"),
                            $"Run {Workers.ToString(CultureInfo.InvariantCulture)} IMMEDIATE writers"
                        ],
                        !_immediateHasRun
                            ? Div.Class("text-secondary mt-3 mb-0")[
                                "One BEGIN IMMEDIATE transaction per writer, all committing via the fair-interval retry."
                            ]
                            : Div
                                .Class($"alert mt-3 mb-0 {(_immediateSucceeded == _immediateAttempted ? "alert-success" : "alert-danger")}")[
                                I
                                    .Class($"bi me-2 {(_immediateSucceeded == _immediateAttempted ? "bi-check-circle" : "bi-exclamation-triangle")}"),
                                $"{_immediateSucceeded.ToString(CultureInfo.InvariantCulture)} of {_immediateAttempted.ToString(CultureInfo.InvariantCulture)} IMMEDIATE writers committed. ",
                                $"Total rows now: {_rowCount.ToString(CultureInfo.InvariantCulture)}."
                            ]
                    ]
                ]
            ]
    ];
}
