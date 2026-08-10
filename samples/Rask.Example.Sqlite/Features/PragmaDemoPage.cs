using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Sqlite.Data;
using Rask.SQLite;

namespace Rask.Example.Sqlite.Features;

// The whole sample on one page: show the live pragma values the connection is actually running with,
// then let the visitor fire a burst of concurrent writers and watch every one commit — the payoff of
// WAL + busy_timeout that a stock `UseSqlite` would turn into "database is locked". The second demo fires
// the same burst through the raw factory's BEGIN IMMEDIATE + non-blocking fair-interval retry.
[Route("/")]
public sealed partial class PragmaDemoPage(
    IDbContextFactory<DemoDbContext> dbContextFactory,
    IRaskSqliteConnectionFactory connectionFactory) : Component
{
    private const int Workers = 25;

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

    private IReadOnlyList<(string Name, string Value)> _pragmas = [];
    private int _rowCount;
    private bool _loaded;
    private int _attempted;
    private int _succeeded;
    private bool _hasRun;
    private int _immediateAttempted;
    private int _immediateSucceeded;
    private bool _immediateHasRun;

    protected override Component? Head => Title["SQLite pragmas — Rask"];

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
