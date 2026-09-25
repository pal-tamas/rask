namespace Rask;

/// <summary>One tab — a link to a view with a URL, or a tab over a panel in the same page.</summary>
/// <remarks>
/// <para>
/// One component for both, because a reader sees one thing. Which it is comes from what it is given:
/// <see cref="Href" /> makes it a real link, <see cref="Name" /> makes it a tab over a
/// <see cref="UiTabPanel" /> inside a <see cref="UiTabGroup" />.
/// </para>
/// <para>
/// <b>Prefer the link.</b> A tab that is a URL is bookmarkable, survives a refresh, answers the back button and
/// works before the runtime boots; a tab that is state in a field is none of those. <see cref="Name" /> is for
/// the views that genuinely have no URL.
/// </para>
/// </remarks>
public sealed partial class UiTab : Component
{
    /// <summary>The words on the tab.</summary>
    public required string Label { get; set; }

    /// <summary>Where it goes, for a view with a URL of its own.</summary>
    public string? Href { get; set; }

    /// <summary>The <see cref="UiTabPanel" /> it shows, for a view inside a <see cref="UiTabGroup" />.</summary>
    public string? Name { get; set; }

    /// <summary>An icon before the label.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>
    ///     Whether this tab is the selected one. Inside a <see cref="UiTabGroup" /> the group answers this, and
    ///     stating it here is ignored.
    /// </summary>
    public bool? Active { get; set; }

    /// <summary>A count shown beside the label, for a tab that filters a list.</summary>
    public string? Count { get; set; }

    /// <summary>Colours the count when it is a number worth acting on.</summary>
    public bool? Alarm { get; set; }

    /// <summary>Draws it as unavailable. Still a link — use it for a view that exists but has nothing in it.</summary>
    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    // A tab registers with its group as it renders, and the render cache cannot see that.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render() =>
        Name is { Length: > 0 } name && Context.Get<UiTabScope>() is { } scope
            ? PanelTab(name, scope)
            : Link();

    private Component Link() =>
        NavLink
            .Href(Href ?? "#")
            // A tab states its own selection — tab-active and aria-selected, from Active — so NavLink's route-derived
            // active state is opted out: a filter tab whose path is the page's would otherwise also claim to be the
            // current page, beside the section tab that is.
            .ActiveClass("")
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = Active == true ? "true" : "false" })
            .Class(TabClass(Active == true))[
            Content()
        ];

    // Inside a group: a real <button>, not a link. There is nowhere for it to go — the panel is already on the
    // page — and a link with href="#" is one the browser will follow, putting a stray fragment in the address
    // bar and breaking the back button it was supposed to protect.
    private Component PanelTab(string name, UiTabScope scope)
    {
        scope.Register(name);
        var selected = string.Equals(scope.Selected, name, StringComparison.Ordinal);

        return Button
            .Id(scope.TabId(name))
            .Type("button")
            .Role("tab")
            .Disabled(Disabled == true)
            // Roving tabindex: only the selected tab is a tab stop, so Tab out of the tablist lands in the
            // PANEL rather than walking every remaining tab. The arrows move between them instead.
            .TabIndex(selected ? 0 : -1)
            .Class(TabClass(selected))
            .Aria(new Dictionary<string, string?>
            {
                ["selected"] = selected ? "true" : "false",
                ["controls"] = scope.PanelId(name),
            })
            .OnClick(() => scope.Select(name))[
            Content()
        ];
    }

    private string TabClass(bool active) =>
        UiClass.Compose(
            "tab gap-2 whitespace-nowrap",
            active ? "tab-active" : "",
            Disabled == true ? "tab-disabled" : "",
            // 44px is the smallest reliable touch target and daisyUI's tab is shorter than that on
            // a phone; the height relaxes from sm up, where there is a pointer.
            "min-h-11 sm:min-h-0",
            Class);

    private Component Content() =>
    [
        Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null,
        Span[Label],
        Count is null
            ? null
            : Span.Class(Alarm == true
                ? "rounded bg-error/10 px-1.5 py-0.5 text-xs tabular-nums text-error"
                : "rounded bg-base-200 px-1.5 py-0.5 text-xs tabular-nums opacity-60")[Count]
    ];
}
