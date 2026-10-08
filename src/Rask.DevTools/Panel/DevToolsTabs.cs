using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The panel's tab strip, with the count of errors not yet seen on the Errors tab — and the hidden element that tells
///     the page the same count, for the dot on the pill, and hears the page asking to show the errors.
/// </summary>
/// <remarks>
///     A component of its own so a new error re-renders the strip, not the whole panel. An error counts as seen once the
///     Errors tab has been on screen with it listed.
/// </remarks>
internal sealed partial class DevToolsTabs : Component
{
    private const string Wire = DevToolsTabIds.Wire;
    private const string Tree = DevToolsTabIds.Tree;
    private const string Renders = DevToolsTabIds.Renders;
    private const string Perf = DevToolsTabIds.Perf;
    private const string Errors = DevToolsTabIds.Errors;

    private DevToolsErrorLog? _page;
    private DevToolsErrorLog? _app;
    private DevToolsRefreshGate? _gate;
    private long _seenPage;
    private long _seenApp;

    /// <summary>The tab on screen.</summary>
    public required string Current { get; set; }

    /// <summary>The inspected page's errors.</summary>
    public required DevToolsErrorLog PageErrors { get; set; }

    /// <summary>The app-wide errors.</summary>
    public required DevToolsErrorLog AppErrors { get; set; }

    /// <summary>Raised with the tab a developer picked, or the Errors tab when the page asks for it.</summary>
    public Callback<string> OnSelect { get; set; }

    // What has been seen is a field, which the render cache cannot see.
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
        var page = PageErrors.Snapshot();
        var app = AppErrors.Snapshot();

        // On the Errors tab, everything listed is being looked at.
        if (IsCurrent(Errors))
        {
            _seenPage = Latest(page, _seenPage);
            _seenApp = Latest(app, _seenApp);
        }

        var unseen = Unseen(page, _seenPage) + Unseen(app, _seenApp);

        return Div.Class("flex items-center gap-1")[
            Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm).Value(Current).OnChange(id => OnSelect.Invoke(id).AsTask())[
                Tab(Wire, "Wire", 0),
                Tab(Tree, "Tree", 0),
                Tab(Renders, "Renders", 0),
                Tab(Perf, "Perf", 0),
                Tab(Errors, "Errors", unseen)
            ],
            // For the panel's script: the count, for the pill's dot, and the page's request to show the errors.
            Span.Hidden(true)
                .Data(
                    ("rask-devtools-errors", unseen.ToString(CultureInfo.InvariantCulture)))
                .OnKeyDown(e => Requested(e.Key))
        ];
    }

    private void OnChanged(object? sender, EventArgs e) => _gate?.Notify();

    private bool IsCurrent(string id) => string.Equals(Current, id, StringComparison.Ordinal);

    private Task Requested(string? key) =>
        string.Equals(key, DevToolsTabIds.ShowErrorsKey, StringComparison.Ordinal) ? OnSelect.Invoke(Errors).AsTask() : Task.CompletedTask;

    private static Component Tab(string id, string label, int count) =>
        Ui.Tab.Key(id).Name(id)[
            label,
            count > 0 ? Ui.Badge.Sm.Solid.Color(Ui.Color.Red)[count.ToString(CultureInfo.InvariantCulture)] : null
        ];

    // Only errors: a warning is listed, but does not call for attention.
    internal static int Unseen(DevToolsError[] errors, long seen)
    {
        var count = 0;
        foreach (var error in errors)
        {
            if (error.Sequence > seen && !error.IsWarning)
            {
                count++;
            }
        }

        return count;
    }

    private static long Latest(DevToolsError[] errors, long seen) =>
        errors.Length == 0 ? seen : Math.Max(seen, errors[^1].Sequence);
}
