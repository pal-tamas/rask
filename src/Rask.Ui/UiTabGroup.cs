using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:tab.group</c>: a <see cref="UiTabs" /> and the <see cref="UiTabPanel" />s it switches
///     between.
/// </summary>
/// <remarks>
///     <para>
///     The group ties each tab to the panel of the same <c>Name</c> — <c>aria-controls</c> one way,
///     <c>aria-labelledby</c> the other — and shows the selected tab's panel. Which tab is selected is the
///     tablist's to say: bind it there (<c>Ui.Tabs.Bind(() =&gt; Tab)</c>), or leave it alone and the row
///     keeps track itself.
///     </para>
///     <code>
///     Ui.TabGroup[
///         Ui.Tabs[
///             Ui.Tab.Name("profile")["Profile"],
///             Ui.Tab.Name("account")["Account"]
///         ],
///         Ui.TabPanel.Name("profile")[ /* … */ ],
///         Ui.TabPanel.Name("account")[ /* … */ ]
///     ]
///     </code>
///     <para>
///     Write the tablist before the panels: a panel learns which tab is selected from the row above it.
///     </para>
/// </remarks>
public sealed partial class UiTabGroup : Component
{
    private static readonly UiPartMarker Marker = new("ui-tab-group");

    // Per instance, so two groups on one page cannot collide on the ids a tab and its panel name each other by.
    private readonly string _prefix = "ui-tabs-" + UiInstanceCounter.Next().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Lets the browser's find-in-page reach the panels that are not shown; a match selects its tab.
    /// </summary>
    public bool? Findable { get; set; }

    public string? Class { get; set; }

    // The scope is rebuilt, and filled by the tablist, on every render — which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render() =>
        // The arrow, not the text caret, over the whole group: a tab row is chrome, as Flux draws it.
        Div.Class(UiClass.Compose("cursor-default", Class)).Data(Marker.With(null))[
            Context.Provide(new UiTabGroupScope(_prefix, Findable == true))[Children ?? []]
        ];
}
