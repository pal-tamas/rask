using System.Globalization;
using Rask;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Errors tab: what went wrong while the page worked — its components' faults, with where on the page they
///     happened, and the framework's warnings and errors — newest first.
/// </summary>
/// <remarks>
///     Two logs feed it: the page's own, and the app-wide one every panel shares, for what the framework reported outside
///     any page's render or handler. A filter picks either or both.
/// </remarks>
internal sealed partial class DevToolsErrorsTab : Component
{
    /// <summary>How many errors the tab lists.</summary>
    internal const int RowLimit = 100;

    private const string All = "all";
    private const string Page = "page";
    private const string App = "app";

    private DevToolsErrorLog? _page;
    private DevToolsErrorLog? _app;
    private DevToolsRefreshGate? _gate;
    private string _filter = All;

    // The report open for editing, by row key, and what the developer has made of each draft so far.
    private string? _reporting;
    private readonly Dictionary<string, (string Title, string Body)> _drafts = [];

    /// <summary>The inspected page's errors.</summary>
    public required DevToolsErrorLog PageErrors { get; set; }

    /// <summary>The app-wide errors every panel shows.</summary>
    public required DevToolsErrorLog AppErrors { get; set; }

    /// <summary>Raised with a component's tree id when the developer asks to see it in the Tree tab.</summary>
    public Callback<long>? OnShowInTree { get; set; }

    /// <summary>What a framework bug report says about where the app runs; without it, no error offers one.</summary>
    public DevToolsBugReport.Environment? ReportEnvironment { get; set; }

    // The filter is a field, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Task OnMount()
    {
        _gate = new DevToolsRefreshGate(StateHasChanged, CancellationToken);
        _page = PageErrors;
        _app = AppErrors;
        _page.Changed += OnChanged;
        _app.Changed += OnChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnUnmount()
    {
        // Both logs outlive the panel; a handler left on either would keep this tab and its session alive.
        if (_page is { } page)
        {
            page.Changed -= OnChanged;
            _page = null;
        }

        if (_app is { } app)
        {
            app.Changed -= OnChanged;
            _app = null;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var errors = Merge(
            _filter == App ? [] : PageErrors.Snapshot(),
            _filter == Page ? [] : AppErrors.Snapshot());

        return Div.Class("flex flex-col gap-3")[
            Div.Class("flex flex-wrap items-center justify-between gap-2")[
                Ui.Join[
                    FilterButton(All, "All"),
                    FilterButton(Page, "This page"),
                    FilterButton(App, "App-wide")
                ],
                Ui.Button.Size(Ui.Size.Sm).Title("Forget the errors listed").OnClick(Clear)["Clear"]
            ],
            errors.Count == 0
                ? Ui.Alert.Tone(Ui.Tone.Success)[
                    _filter == App
                        ? "Nothing reported outside a page's work."
                        : "No errors. A component that throws, and anything the framework warns about, is listed here."
                ]
                : Div.Class("flex flex-col gap-2")[
                    errors.Take(RowLimit).Select(Row).ToArray()
                ]
        ];
    }

    private void OnChanged() => _gate?.Notify();

    private void Clear()
    {
        if (_filter != App)
        {
            PageErrors.Clear();
        }

        if (_filter != Page)
        {
            AppErrors.Clear();
        }
    }

    private Component FilterButton(string id, string label) =>
        Ui.Button
            .Size(Ui.Size.Sm)
            // daisyUI's own markers, written whole: a composed class name is invisible to the kit's Tailwind scan.
            .Class(_filter == id ? "join-item btn-active" : "join-item")
            .Aria(new Dictionary<string, string?> { ["pressed"] = _filter == id ? "true" : "false" })
            .OnClick(() => _filter = id)[label];

    /// <summary>Both logs, newest first.</summary>
    internal static List<DevToolsError> Merge(DevToolsError[] page, DevToolsError[] app)
    {
        var merged = new List<DevToolsError>(page.Length + app.Length);
        merged.AddRange(page);
        merged.AddRange(app);
        merged.Sort(static (a, b) => b.At.CompareTo(a.At));
        return merged;
    }

    private static string RowKey(DevToolsError error) =>
        (error.AppWide ? "app-" : "page-") + error.Sequence.ToString(CultureInfo.InvariantCulture);

    private Component Row(DevToolsError error) =>
        Ui.Card.Key(RowKey(error))
            .Class("card-border card-sm")[
                Div.Class("card-body gap-1")[
                    Div.Class("flex flex-wrap items-center gap-2")[
                        Ui.Badge.Size(Ui.Size.Sm).Tone(error.IsWarning ? Ui.Tone.Warning : Ui.Tone.Error)[KindLabel(error)],
                        Span.Class("font-mono font-semibold")[error.Title],
                        error.Count > 1 ? Ui.Badge.Size(Ui.Size.Sm)["×" + error.Count.ToString(CultureInfo.InvariantCulture)] : null,
                        error.AppWide ? Ui.Badge.Size(Ui.Size.Sm)["app-wide"] : null,
                        CaughtLabel(error) is { } caught ? Span.Class("text-xs opacity-60")[caught] : null,
                        Span.Class("text-xs opacity-60 tabular-nums")[error.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture)]
                    ],
                    P.Class("text-sm whitespace-pre-wrap")[error.Message],
                    error.Path.Count == 0
                        ? null
                        : Div.Class("flex flex-wrap items-center gap-2 text-xs")[
                            Span.Class("font-mono opacity-80")[string.Join(" › ", error.Path)],
                            error.ComponentId is { } id && OnShowInTree is { } show
                                ? Ui.Button.Size(Ui.Size.Xs).OnClick(() => show.Invoke(id) ?? Task.CompletedTask)["Show in tree"]
                                : null
                        ],
                    error.Detail is { } detail
                        ? Ui.Collapse.Title("Stack")[Pre.Class("text-xs whitespace-pre-wrap break-all")[detail]]
                        : null,
                    error.LikelyFrameworkBug && ReportEnvironment is { } environment
                        ? Report(error, environment)
                        : null
                ]
            ];

    // An error whose stack points at Rask: a button, and once pressed, the report to review before GitHub sees any of it.
    private Component Report(DevToolsError error, DevToolsBugReport.Environment environment)
    {
        var key = RowKey(error);
        if (_reporting != key)
        {
            return Div.Class("flex flex-wrap items-center gap-2 text-xs")[
                Span.Class("opacity-60")["This looks like a bug in Rask itself."],
                Ui.Button.Size(Ui.Size.Xs).OnClick(() => _reporting = key)["Report framework bug"]
            ];
        }

        if (!_drafts.TryGetValue(key, out var draft))
        {
            draft = DevToolsBugReport.Draft(error, environment);
            _drafts[key] = draft;
        }

        return Div.Class("flex flex-col gap-2")[
            P.Class("text-xs opacity-60")[
                "Check what it says and add what you can. Nothing leaves this machine until you submit the issue on GitHub; "
                + "the exception's message, props, data and file paths are not in it."
            ],
            Ui.Input.Value(draft.Title).Label("Title").OnInput(v => _drafts[key] = (v, _drafts[key].Body)),
            Ui.Textarea.Value(draft.Body).Label("Issue").Rows(12).Class("font-mono text-xs")
                .OnInput(v => _drafts[key] = (_drafts[key].Title, v)),
            Div.Class("flex flex-wrap items-center gap-2")[
                A.Class("btn btn-primary btn-sm")
                    .Href(DevToolsBugReport.IssueUrl(draft.Title, draft.Body))
                    .Target("_blank")
                    .Rel("noopener noreferrer")["Open the issue on GitHub"],
                Ui.Button.Size(Ui.Size.Sm).OnClick(() => _reporting = null)["Cancel"]
            ]
        ];
    }

    internal static string KindLabel(DevToolsError error) => error.Kind switch
    {
        DevToolsErrorKind.Render => "render",
        DevToolsErrorKind.Handler => "handler",
        DevToolsErrorKind.Lifecycle => "lifecycle",
        DevToolsErrorKind.Page => "page script",
        DevToolsErrorKind.Island => "island",
        _ => error.IsWarning ? "warning" : "error",
    };

    // Whether a boundary took it, for the faults where that is a question: a render fault always ends at one, and a
    // diagnostic is not an exception anything could catch.
    private static string? CaughtLabel(DevToolsError error) => error.Kind switch
    {
        DevToolsErrorKind.Handler or DevToolsErrorKind.Lifecycle => error.Caught
            ? "caught by an error boundary"
            : "no error boundary caught it",
        _ => null,
    };
}
