namespace Rask;

/// <summary>What a <see cref="UiTab" /> shows, inside a <see cref="UiTabGroup" />.</summary>
/// <remarks>
/// <para>
/// Matched to its tab by <see cref="Name" />, and tied to it both ways: the tab's <c>aria-controls</c> names
/// this, and this names the tab back through <c>aria-labelledby</c>, so a screen reader announces the panel
/// with the words on the tab that opened it.
/// </para>
/// <para>
/// Every panel is RENDERED and the ones not shown carry <c>hidden</c>, rather than being left out of the markup
/// altogether. Their content is then findable by the browser's own in-page search and by a screen reader's
/// virtual cursor, and switching tabs shows markup that is already there rather than building it — which is
/// also what keeps the diff between two tabs small.
/// </para>
/// </remarks>
public sealed partial class UiTabPanel : Component
{
    /// <summary>The <see cref="UiTab.Name" /> this panel belongs to.</summary>
    public required string Name { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiTabScope>() is not { } scope)
        {
            // Outside a group there is no tab to be tied to and nothing to hide it, so it is its own content —
            // which is what a panel lifted out of a group during a refactor should do rather than vanish.
            return Div.Class(Class)[Children ?? []];
        }

        var shown = string.Equals(scope.Selected, Name, StringComparison.Ordinal);
        var panel = Div
            .Id(scope.PanelId(Name))
            .Role("tabpanel")
            // A panel is a tab stop so that Tab out of the tablist lands IN what was opened, which is where a
            // reader is going — otherwise focus skips past the content to whatever follows the group.
            .TabIndex(0)
            .Class(Class)
            .Aria("labelledby", scope.TabId(Name));

        return (shown ? panel : panel.Hidden(true))[Children ?? []];
    }
}
