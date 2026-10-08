namespace Rask;

/// <summary>
///     Flux's <c>flux:tab.panel</c>: what the <see cref="UiTab" /> of the same <see cref="Name" /> shows, in a
///     <see cref="UiTabGroup" />.
/// </summary>
/// <remarks>
///     <para>
///     Tied to its tab both ways: the tab's <c>aria-controls</c> names this, and this names the tab back
///     through <c>aria-labelledby</c>, so a screen reader announces the panel with the words on the tab.
///     </para>
///     <para>
///     Every panel is rendered and the ones not shown are <c>hidden</c>, so switching tabs shows markup
///     that is already there. The shown panel is a tab stop: Tab leaves the row for what it opened.
///     </para>
/// </remarks>
public sealed partial class UiTabPanel : Component
{
    private static readonly UiPartMarker Marker = new("ui-tab-panel");
    private static readonly UiPartMarker SelectedMarker = Marker.And("selected");

    /// <summary>The <see cref="UiTab.Name" /> this panel belongs to.</summary>
    public required string Name { get; set; }

    /// <summary>
    ///     Shows this panel when no tablist says which is selected — a panel written outside a group, or
    ///     ahead of its tabs.
    /// </summary>
    public bool? Selected { get; set; }

    public string? Class { get; set; }

    // Which panel is shown is the tablist's state, read through the group's scope.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var group = Context.Get<UiTabGroupScope>();
        var tabs = group?.Tabs;
        var shown = Shown(group, tabs);

        var panel = Div
            .Id(group?.PanelId(Name))
            .Class(UiClass.Compose(PanelClass(shown, group?.Findable == true), Class))
            .Data((shown ? SelectedMarker : Marker).With(null))
            .Role("tabpanel")
            .TabIndex(shown ? 0 : -1);

        if (group is not null)
        {
            panel = panel.Aria("labelledby", group.TabId(Name));
        }

        if (shown)
        {
            return panel[Children ?? []];
        }

        // `hidden="until-found"` keeps the words where find-in-page can reach them, and the browser raises
        // `beforematch` on the panel it is about to reveal — which is the moment to select its tab.
        return group is { Findable: true } && tabs is not null
            ? panel.Attributes(("hidden", "until-found")).OnBeforeMatch(_ => tabs.Select(Name))[Children ?? []]
            : panel.Hidden(true)[Children ?? []];
    }

    private bool Shown(UiTabGroupScope? group, UiTabScope? tabs)
    {
        if (tabs is not null)
        {
            return string.Equals(tabs.Selected, Name, StringComparison.Ordinal);
        }

        // Outside a group there is nothing to hide it, so it is its own content unless told otherwise —
        // which is what a panel lifted out of a group during a refactor should do rather than vanish.
        return group is null ? Selected != false : Selected == true;
    }

    // An until-found panel still takes its padding's room, so it is lifted out of the flow and made inert.
    private static string PanelClass(bool shown, bool findable) => (shown, findable) switch
    {
        (false, true) => "pt-8 absolute opacity-0 pointer-events-none",
        _ => "pt-8",
    };
}
