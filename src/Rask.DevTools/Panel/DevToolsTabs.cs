using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The panel's tab ids. A class of their own: inside markup, a component's name is its chain entry, not the type, so
///     constants on the component itself are out of reach there.
/// </summary>
internal static class DevToolsTabIds
{
    internal const string Wire = "wire";
    internal const string Tree = "tree";
    internal const string Renders = "renders";
    internal const string Perf = "perf";
    internal const string Errors = "errors";

    /// <summary>The <c>key</c> of the keydown the page's "Open in DevTools" arrives as.</summary>
    internal const string ShowErrorsKey = "errors:show";
}

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
        if (Current == Errors)
        {
            _seenPage = Latest(page, _seenPage);
            _seenApp = Latest(app, _seenApp);
        }

        var unseen = Unseen(page, _seenPage) + Unseen(app, _seenApp);

        return Div.Class("flex items-center gap-1")[
            Div.Role("tablist").Class("flex flex-wrap items-center gap-1")[
                TabButton(Wire, "Wire", 0),
                TabButton(Tree, "Tree", 0),
                TabButton(Renders, "Renders", 0),
                TabButton(Perf, "Perf", 0),
                TabButton(Errors, "Errors", unseen)
            ],
            // For the panel's script: the count, for the pill's dot, and the page's request to show the errors.
            Span.Hidden(true)
                .Data(new Dictionary<string, string?>
                {
                    ["rask-devtools-errors"] = unseen.ToString(CultureInfo.InvariantCulture),
                })
                .OnKeyDown(e => Requested(e.Key))
        ];
    }

    private void OnChanged() => _gate?.Notify();

    private Task Requested(string? key) =>
        key == DevToolsTabIds.ShowErrorsKey ? OnSelect.Invoke(Errors).AsTask() : Task.CompletedTask;

    private Component TabButton(string id, string label, int count) =>
        Ui.Button
            .Key(id)
            .Size(Ui.Size.Sm)
            .Role("tab")
            // daisyUI's own marker, written whole: a composed class name is invisible to the kit's Tailwind scan.
            .Class(Current == id ? "btn-active" : null)
            .Aria(new Dictionary<string, string?> { ["selected"] = Current == id ? "true" : "false" })
            .OnClick(() => OnSelect.Invoke(id).AsTask())[
                label,
                count > 0 ? Ui.Badge.Size(Ui.Size.Xs).Tone(Ui.Tone.Error)[count.ToString(CultureInfo.InvariantCulture)] : null
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
