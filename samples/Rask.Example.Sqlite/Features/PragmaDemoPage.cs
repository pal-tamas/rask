using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Routing;
using Rask.Example.Sqlite.Data;

namespace Rask.Example.Sqlite.Features;

// The whole sample on one page: show the live pragma values the connection is actually running with,
// then let the visitor fire a burst of concurrent writers and watch every one commit — the payoff of
// WAL + busy_timeout that a stock `UseSqlite` would turn into "database is locked".
[Route("/")]
public sealed class PragmaDemoPage(IDbContextFactory<DemoDbContext> dbContextFactory) : Component
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

    private IReadOnlyList<(string Name, string Value)> _pragmas = [];
    private int _rowCount;
    private bool _loaded;
    private int _attempted;
    private int _succeeded;
    private bool _hasRun;

    protected override Component? Head => Title()["SQLite pragmas — Rask"];

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

    private static string ReadPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    protected override Component? Render() =>
    [
        Div(Class: "mb-4")[
            H1(Class: "h3 mb-1")["SQLite production pragmas"],
            P(Class: "text-secondary mb-0")[
                "One line — ", Code()["UseRaskSqlite"],
                " — puts the Rails 8 production pragma set on every connection."
            ]
        ],

        // Code above, live result below.
        Div(Class: "card shadow-sm mb-4")[
            Div(Class: "card-header bg-dark text-light py-2")[
                I(Class: "bi bi-code-slash me-2"), "Program.cs"
            ],
            Pre(Class: "mb-0 p-3 bg-dark text-light rounded-bottom overflow-auto")[
                Code()[WiringSnippet]
            ]
        ],

        !_loaded
            ? Div(Class: "text-secondary")[Span(Class: "spinner-border spinner-border-sm me-2"), "Loading…"]
            : Div()[
                Div(Class: "card shadow-sm mb-4")[
                    Div(Class: "card-header py-2 fw-semibold")[
                        I(Class: "bi bi-sliders me-2"), "Live pragma values on this connection"
                    ],
                    Table(Class: "table table-striped align-middle mb-0")[
                        Thead()[Tr()[Th()["PRAGMA"], Th(Class: "text-end")["Value"]]],
                        Tbody()[
                            _pragmas.Select(p => Tr(Key: p.Name)[
                                Td(Class: "fw-semibold font-monospace")[p.Name],
                                Td(Class: "text-end font-monospace")[p.Value]
                            ])
                        ]
                    ]
                ],

                Div(Class: "card shadow-sm")[
                    Div(Class: "card-body")[
                        H2(Class: "h5")["Concurrent writers"],
                        P(Class: "text-secondary")[
                            $"Fire {Workers.ToString(CultureInfo.InvariantCulture)} writers at the database at once. ",
                            "WAL lets readers and the writer coexist, and the 5-second ",
                            Code()["busy_timeout"],
                            " absorbs the momentary write-lock contention — so every writer commits."
                        ],
                        Button("button", Class: "btn btn-primary", OnClickAsync: RunWritersAsync)[
                            I(Class: "bi bi-lightning-charge me-1"),
                            $"Run {Workers.ToString(CultureInfo.InvariantCulture)} concurrent writers"
                        ],
                        !_hasRun
                            ? Div(Class: "text-secondary mt-3 mb-0")[
                                $"Total rows written so far: {_rowCount.ToString(CultureInfo.InvariantCulture)}."
                            ]
                            : Div(Class: $"alert mt-3 mb-0 {(_succeeded == _attempted ? "alert-success" : "alert-danger")}")[
                                I(Class: $"bi me-2 {(_succeeded == _attempted ? "bi-check-circle" : "bi-exclamation-triangle")}"),
                                $"{_succeeded.ToString(CultureInfo.InvariantCulture)} of {_attempted.ToString(CultureInfo.InvariantCulture)} writers committed. ",
                                $"Total rows now: {_rowCount.ToString(CultureInfo.InvariantCulture)}."
                            ]
                    ]
                ]
            ]
    ];
}
