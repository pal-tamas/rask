using System.Globalization;
using Rask.Core.Routing;
using Rask.Example.Crdt.Data;
using Rask.Example.Crdt.Devices;
using Rask.SQLite.Crdt.Sync;

namespace Rask.Example.Crdt.Features;

// Three devices of one family, each with its own SQLite database, sharing a bucket and nothing else.
// The point to see: take two devices offline, edit DIFFERENT FIELDS of the same todo on each, then
// bring both back and sync — both edits survive, because merging is per column rather than per row.
public sealed partial class FamilyTodosPage(FamilyDevices family) : Page
{
    protected override string Route => "/";

    private const string WiringSnippet =
        """
        options.UseSqlite($"Data Source={file};Pooling=False")
               .UseRaskCrdt(o => o.ExtensionPath = crsqlitePath);

        protected override void OnModelCreating(ModelBuilder b) => b.ApplyCrdtConventions();
        await context.PromoteToCrrsAsync();

        var engine = new CrdtSyncEngine(objectStore, new CrdtChangeFeed(context));
        await engine.SyncAsync();          // publish mine, then apply everyone else's
        """;

    private readonly Dictionary<string, IReadOnlyList<Todo>> _todos = [];
    private readonly Dictionary<string, string> _drafts = [];

    protected override Component? HeadAssets => Title()["Family todos — Rask"];

    protected override async Task OnMountAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        foreach (var device in family.All)
        {
            _todos[device.Name] = await device.ReadAsync();
        }
    }

    private async Task AddAsync(FamilyDevice device)
    {
        var title = _drafts.GetValueOrDefault(device.Name, string.Empty).Trim();
        if (title.Length == 0)
        {
            return;
        }

        await device.AddAsync(title);
        _drafts[device.Name] = string.Empty;
        await RefreshAsync();
    }

    private async Task SyncAsync(FamilyDevice device)
    {
        await device.SyncAsync();
        await RefreshAsync();
    }

    private async Task SyncEveryoneAsync()
    {
        // Twice around: the first pass publishes everyone's work, the second lets each device pick up
        // what the others published during the first. Merging is commutative, so the order does not
        // matter — only that every device has had a turn after every other has published.
        for (var round = 0; round < 2; round++)
        {
            foreach (var device in family.All)
            {
                await device.SyncAsync();
            }
        }

        await RefreshAsync();
    }

    protected override Component? Render() =>
    [
        Div(Class: "mb-4")[
            H1(Class: "h3 mb-1")["A shared database with no server"],
            P(Class: "text-secondary mb-0")[
                "Three devices, three SQLite databases, one bucket — and nothing in between. ",
                "Edit ", Strong()["different fields of the same todo"],
                " on two devices while both are offline, then sync: both edits survive."
            ]
        ],

        Div(Class: "card shadow-sm mb-4")[
            Div(Class: "card-header bg-dark text-light py-2")[
                I(Class: "bi bi-code-slash me-2"), "The whole wiring"
            ],
            Pre(Class: "mb-0 p-3 bg-dark text-light rounded-bottom overflow-auto")[Code()[WiringSnippet]]
        ],

        family.Available ? Devices() : Setup()
    ];

    private Component Setup() =>
        Div(Class: "alert alert-warning", Data: Test("setup"))[
            H2(Class: "h5 alert-heading")[
                I(Class: "bi bi-exclamation-triangle me-2"), "cr-sqlite is not configured"
            ],
            P()[family.SetupHint ?? string.Empty],
            Pre(Class: "mb-0")[
                Code()["RASK_CRSQLITE_PATH=/path/to/crsqlite.dylib dotnet run --project samples/Rask.Example.Crdt"]
            ]
        ];

    private Component Devices() =>
        Div()[
            Div(Class: "d-flex align-items-center gap-3 mb-3")[
                Button("button", Class: "btn btn-primary", Data: Test("sync-all"),
                    OnClickAsync: SyncEveryoneAsync)[
                    I(Class: "bi bi-arrow-repeat me-1"), "Sync everyone"
                ],
                Span(Class: "text-secondary small")[
                    "The bucket is a folder: ", Code()[family.BucketPath]
                ]
            ],

            Div(Class: "row g-3")[
                family.All.Select(device => Div(Class: "col-lg-4", Key: device.Name)[Card(device)])
            ]
        ];

    private Component Card(FamilyDevice device) =>
        Div(Class: "card shadow-sm h-100", Data: Test($"device-{device.Name}"))[
            Div(Class: "card-header d-flex justify-content-between align-items-center py-2")[
                Span(Class: "fw-semibold")[I(Class: "bi bi-phone me-2"), device.Name],
                Button("button",
                    Class: device.Link.Online ? "btn btn-sm btn-outline-success" : "btn btn-sm btn-outline-secondary",
                    Data: Test($"link-{device.Name}"),
                    OnClick: () => device.Link.Online = !device.Link.Online)[
                    I(Class: device.Link.Online ? "bi bi-wifi me-1" : "bi bi-wifi-off me-1"),
                    device.Link.Online ? "Online" : "Offline"
                ]
            ],

            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 mb-3")[
                    Input(
                        Type: InputType.Text,
                        Class: "form-control form-control-sm",
                        Value: _drafts.GetValueOrDefault(device.Name, string.Empty),
                        Placeholder: "add a todo",
                        Data: Test($"draft-{device.Name}"),
                        OnInput: v => _drafts[device.Name] = v),
                    Button("button", Class: "btn btn-sm btn-outline-primary", Data: Test($"add-{device.Name}"),
                        OnClickAsync: () => AddAsync(device))["Add"]
                ],

                Ul(Class: "list-group list-group-flush mb-3")[
                    _todos.GetValueOrDefault(device.Name, []).Select(todo =>
                        Li(Class: "list-group-item d-flex justify-content-between align-items-center px-0",
                            Key: todo.Id.ToString())[
                            Span(Class: todo.Done ? "text-decoration-line-through text-secondary" : null)[
                                todo.Title
                            ],
                            Span(Class: "d-flex gap-1")[
                                Button("button", Class: "btn btn-sm btn-outline-secondary",
                                    OnClickAsync: async () =>
                                    {
                                        await device.BumpPriorityAsync(todo.Id);
                                        await RefreshAsync();
                                    })[
                                    "P", todo.Priority.ToString(CultureInfo.InvariantCulture)
                                ],
                                Button("button", Class: "btn btn-sm btn-outline-secondary",
                                    OnClickAsync: async () =>
                                    {
                                        await device.ToggleAsync(todo.Id);
                                        await RefreshAsync();
                                    })[
                                    I(Class: todo.Done ? "bi bi-arrow-counterclockwise" : "bi bi-check2")
                                ]
                            ]
                        ])
                ],

                Button("button", Class: "btn btn-sm btn-outline-dark w-100", Data: Test($"sync-{device.Name}"),
                    OnClickAsync: () => SyncAsync(device))[
                    I(Class: "bi bi-arrow-repeat me-1"), "Sync this device"
                ]
            ],

            Div(Class: "card-footer py-2 small text-secondary", Data: Test($"status-{device.Name}"))[
                Describe(device.Status)
            ]
        ];

    // Offline is not an error state here: the edit is already committed to this device's own database,
    // and the next sync sends it. Saying "failed" would train people to ignore the one indicator that
    // actually matters.
    private static string Describe(CrdtSyncStatus status) => status.Phase switch
    {
        CrdtSyncPhase.Idle => "not synced yet",
        CrdtSyncPhase.Syncing => "syncing…",
        CrdtSyncPhase.Offline => "offline — your edits are saved and will sync later",
        _ => $"synced · sent {status.Published.ToString(CultureInfo.InvariantCulture)}"
             + $" · received {status.Received.ToString(CultureInfo.InvariantCulture)}"
             + $" · {status.Peers.ToString(CultureInfo.InvariantCulture)} peer(s)",
    };

    private static Dictionary<string, string?> Test(string id) => new() { ["testid"] = id };
}
