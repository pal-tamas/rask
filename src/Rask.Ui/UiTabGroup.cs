namespace Rask.Ui;

/// <summary>
/// Tabs over panels in one page: the state, the keyboard and the ARIA that ties a tab to what it shows.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's tab group. Put a <see cref="UiTabs" /> inside it holding <see cref="UiTab" />s with a
/// <see cref="UiTab.Name" />, then a <see cref="UiTabPanel" /> per name:
/// </para>
/// <code>
/// UiTabGroup.Selected(_tab).OnSelect(t => _tab = t)[
///     UiTabs[
///         UiTab.Label("Details").Name("details"),
///         UiTab.Label("History").Name("history")
///     ],
///     UiTabPanel.Name("details")[ /* … */ ],
///     UiTabPanel.Name("history")[ /* … */ ]
/// ]
/// </code>
/// <para>
/// The <see cref="UiTabs" /> is not ceremony: a <c>tablist</c> may contain only tabs, so the panels cannot be
/// its siblings — and it is the same structure Flux uses, for the same reason.
/// </para>
/// <para>
/// <b>Reach for links first.</b> A <see cref="UiTab" /> with an <c>Href</c> is a real URL: bookmarkable,
/// survives a refresh, answers the back button and works before the runtime boots. This is for the views that
/// genuinely have no URL — a detail panel beside a record, a settings pane — where an address would be
/// inventing state the page does not have.
/// </para>
/// <para>
/// The keyboard is the tabs pattern: ArrowLeft/ArrowRight move and SHOW as they go, Home and End jump to the
/// ends, and Tab leaves the tablist for the panel rather than walking every tab. Only the selected tab is a tab
/// stop, which is what makes that last part true.
/// </para>
/// </remarks>
public sealed partial class UiTabGroup : Component
{
    // Per instance, so two groups on one page cannot collide on tab and panel ids — aria-controls and
    // aria-labelledby name them across the gap between a tab and what it shows.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private string? _selected;

    /// <summary>
    ///     Which tab is shown, by <see cref="UiTab.Name" />. Unset, the group shows the first tab and keeps track
    ///     itself.
    /// </summary>
    public string? Selected { get; set; }

    /// <summary>Runs when a tab is chosen, with the name of the tab now shown.</summary>
    public Callback<string>? OnSelect { get; set; }

    public string? Class { get; set; }

    // The registered tabs are a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var prefix = "uitg-" + _instance.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var scope = new UiTabScope(prefix, Selected ?? _selected, SelectAsync);

        return Div.Class(UiClass.Compose("flex flex-col gap-4", Class))[
            Context.Provide(scope)[Children ?? []]
        ];
    }

    private async Task SelectAsync(string name)
    {
        // Uncontrolled: the group remembers it itself, so a page that does not care about the tab does not have
        // to hold a field for it. Controlled: Selected is the answer and this only reports the ask.
        _selected = name;
        if (OnSelect is { } onSelect)
        {
            await (onSelect.Invoke(name) ?? Task.CompletedTask).ConfigureAwait(false);
        }
    }
}
