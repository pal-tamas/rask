namespace Rask.Ui;

/// <summary>
/// A button that opens a panel beneath it.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Open" /> decides who owns the open state, and it has three settings rather than two.</b>
/// Left unset, the dropdown is UNCONTROLLED: daisyUI opens it on <c>:focus-within</c> and closes it when
/// focus leaves, which needs no state and no script. Set to <c>true</c> or <c>false</c> it is
/// CONTROLLED — the page decides, and <see cref="OnToggle" /> is how the page hears that the reader
/// asked for a change.
/// </para>
/// <para>
/// The two settings are not the same class. Open writes <c>dropdown-open</c>; closed writes
/// <c>dropdown-close</c>, which daisyUI ranks ABOVE <c>:focus-within</c> — without it, tabbing into the
/// panel would re-open a dropdown the page had just closed, and the state in C# and the state on screen
/// would disagree with nothing reporting it.
/// </para>
/// <para>
/// This used to be a <c>&lt;details&gt;</c> element, which opened and closed with no runtime at all. What
/// that could not do is tell C# anything: a page could neither read the open state nor set it, so a
/// dropdown could not be closed when the action inside it completed.
/// </para>
/// </remarks>
public sealed partial class UiDropdown : Component
{
    /// <summary>The label on the button that opens it.</summary>
    public required string Trigger { get; set; }

    /// <summary>Which side of the trigger the panel opens on.</summary>
    public UiPlacement? Placement { get; set; }

    /// <summary>
    ///     Whether the panel is open. Leave it unset to let the browser handle opening on focus; set it
    ///     to take ownership, and pair it with <see cref="OnToggle" />.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>
    ///     What opens it while uncontrolled. <see cref="UiOpenOn.Hover" /> is pointer-only, so prefer the
    ///     default for anything that has to be reachable by keyboard or touch.
    /// </summary>
    public UiOpenOn? OpenOn { get; set; }

    /// <summary>Runs when the trigger is activated, with the state the reader is asking for.</summary>
    public Action<bool>? OnToggle { get; set; }

    public UiIconName? Icon { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var trigger = Button
            .Type("button")
            .Class("btn")
            .Aria(new Dictionary<string, string?>
            {
                ["haspopup"] = "menu",
                ["expanded"] = Open == true ? "true" : "false",
            });

        // ONLY while uncontrolled, and this is not a detail.
        //
        // daisyUI scopes several rules to `[tabindex]:first-child`, and one of them is
        //     :is(.dropdown.dropdown-open, .dropdown:focus-within) > [tabindex]:first-child
        //         { pointer-events: none }
        // — the trigger stops taking clicks while the panel is open. That is right for the uncontrolled
        // dropdown, where the way out is to click away and let focus leave; a trigger that stayed
        // clickable would fight its own :focus-within rule and never close.
        //
        // It is exactly wrong for a controlled one. The page's only way to close is OnToggle, OnToggle
        // only fires on a click, and the click cannot land — so the dropdown opens once and is stuck.
        // A <button> is focusable with or without the attribute, so dropping it costs the uncontrolled
        // behaviour nothing and buys the controlled one its way back.
        if (Open is null)
        {
            trigger = trigger.TabIndex(0);
        }

        if (OnToggle is { } toggle)
        {
            var next = Open != true;
            trigger = trigger.OnClick(() => toggle(next));
        }

        return Div.Class(UiClass.Compose(
            "dropdown",
            Placement is { } placement ? UiClassNames.DropdownPlacement(placement) : "",
            OpenOn == UiOpenOn.Hover ? "dropdown-hover" : "",
            // Nothing when uncontrolled, so the browser's own focus behaviour stands.
            Open switch { true => "dropdown-open", false => "dropdown-close", null => "" },
            Class))[
            trigger[
                Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
                Span[Trigger]
            ],
            Ul.Class("dropdown-content menu z-1 w-52 rounded-box bg-base-100 p-2 shadow-sm")[
                Children ?? []
            ]
        ];
    }
}

/// <summary>
/// Two pieces of content, one shown at a time.
/// </summary>
/// <remarks>
/// <para>
/// Typical use is an icon that changes when something is on — a menu button becoming a close button, a
/// sound icon becoming a muted one.
/// </para>
/// <para>
/// The state is C#'s: <see cref="Active" /> chooses the face and <see cref="OnChange" /> reports the
/// press. daisyUI's <c>swap-active</c> is what draws it, so no hidden checkbox is involved — which
/// matters for more than tidiness. The checkbox version kept its own state in the DOM, so a swap whose
/// meaning had changed underneath it (the sound was muted by something else) went on showing the old
/// face, and nothing could correct it.
/// </para>
/// <para>
/// It renders a <c>&lt;button&gt;</c>. The checkbox version rendered a <c>&lt;label&gt;</c>, which is
/// only focusable because of the input inside it; with the input gone a label would have been an
/// unreachable control, and a button is what this always was.
/// </para>
/// </remarks>
public sealed partial class UiSwap : Component
{
    /// <summary>
    ///     The accessible name; both faces are decorative once it is set. Not <c>Label</c>, which the
    ///     base type carries as a markup entry.
    /// </summary>
    public required string AccessibleLabel { get; set; }

    /// <summary>The face shown while <see cref="Active" />.</summary>
    public required Component On { get; set; }

    /// <summary>The face shown otherwise.</summary>
    public required Component Off { get; set; }

    /// <summary>Which face is showing.</summary>
    public bool? Active { get; set; }

    public UiSwapAnimation? Animation { get; set; }

    /// <summary>Runs when it is pressed, with the state the reader is asking for.</summary>
    public Action<bool>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                "swap",
                Animation is { } animation ? UiClassNames.SwapAnimation(animation) : "",
                Active == true ? "swap-active" : "",
                Class))
            .Aria(new Dictionary<string, string?>
            {
                ["label"] = AccessibleLabel,
                ["pressed"] = Active == true ? "true" : "false",
            });

        if (OnChange is { } change)
        {
            var next = Active != true;
            button = button.OnClick(() => change(next));
        }

        return button[
            Div.Class("swap-on")[On],
            Div.Class("swap-off")[Off]
        ];
    }
}

/// <summary>
/// A control that selects a theme.
/// </summary>
/// <remarks>
/// <para>
/// It reports a choice and draws itself as chosen; it does not apply the theme, because it cannot. The
/// palette is set by <c>data-theme</c> on the element carrying
/// <see cref="UiStylesheet.ThemeScopeAttribute" />, which is an ANCESTOR — usually <c>&lt;html&gt;</c>,
/// from the root component's <c>Shell</c> override — and no component can write an attribute onto
/// something above it. The page holds the chosen theme and puts <c>UiTheme.Value(theme)</c> there.
/// </para>
/// <para>
/// This is what replaced daisyUI's <c>theme-controller</c> input, which flipped the palette from CSS
/// alone by matching <c>input.theme-controller[value=x]:checked</c>. That was genuinely free — no script,
/// no state — and it is worth being clear about what the trade bought: because nothing in C# knew which
/// theme was showing, the choice could not be persisted, could not be read back, and reset itself on
/// every navigation. A page that owns the value can store it.
/// </para>
/// </remarks>
public sealed partial class UiThemeController : Component
{
    /// <summary>
    ///     What this control is called — for example "Dark". Free to use here because this component
    ///     renders no <c>&lt;label&gt;</c> element of its own.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>The theme this control selects.</summary>
    public required UiThemeName Theme { get; set; }

    /// <summary>Whether this is the theme currently showing.</summary>
    public bool? Active { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Runs when it is chosen, with the theme the reader asked for.</summary>
    public Action<UiThemeName>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                "btn",
                Size is { } size ? UiClassNames.ButtonSize(size) : "",
                Active == true ? "btn-active" : "",
                Class))
            .Aria(new Dictionary<string, string?> { ["pressed"] = Active == true ? "true" : "false" });

        if (OnChange is { } change)
        {
            button = button.OnClick(() => change(Theme));
        }

        return button[Span[Label]];
    }
}

/// <summary>
/// A round action pinned to the corner of the viewport, with more actions behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This one is opened by the browser, not by C#, and that is daisyUI's design rather than a
/// shortcut.</b> The extra actions are hidden by <c>visibility</c> and revealed by
/// <c>.fab:focus-within</c>; daisyUI defines no <c>fab-open</c> class, so there is no class a page could
/// write to force the state. Rendering the actions only when a C# flag says so does not work either:
/// they would still be <c>visibility: hidden</c> until focus arrived, so the flag and the screen would
/// disagree.
/// </para>
/// <para>
/// Opening on focus is keyboard-reachable and touch-reachable, which is why it is an acceptable place
/// for the kit to stop. What it costs is programmatic control: a page cannot open this from code, and
/// cannot close it when an action completes — the browser closes it when focus leaves. Where that
/// matters, a <see cref="UiDropdown" /> with <c>Placement</c> is the controllable shape.
/// </para>
/// </remarks>
public sealed partial class UiFab : Component
{
    /// <summary>The accessible name of the button that opens it.</summary>
    public required string AccessibleLabel { get; set; }

    /// <summary>The icon on the closed button.</summary>
    public UiIconName? Icon { get; set; }

    /// <summary>
    ///     The one action the button itself becomes once open, drawn over the trigger. Omit it and the
    ///     trigger simply fades as the others appear.
    /// </summary>
    public Component? MainAction { get; set; }

    /// <summary>A dismiss drawn over the trigger while open.</summary>
    public Component? Close { get; set; }

    /// <summary>Fans the actions out in an arc instead of stacking them in a column.</summary>
    public bool? Flower { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("fab", Flower == true ? "fab-flower" : "", Class))[
            // FIRST child, and it must carry a tabindex: daisyUI selects the trigger as
            // `[tabindex]:first-child`, and `:focus-within` is what opens the whole thing.
            Div
                .TabIndex(0)
                .Role("button")
                .Class("btn btn-lg btn-circle")
                .Aria(new Dictionary<string, string?> { ["label"] = AccessibleLabel })[
                Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0") : null
            ],
            Close is null ? null : Div.Class("fab-close")[Close],
            MainAction is null ? null : Div.Class("fab-main-action")[MainAction],
            Children ?? []
        ];
}

/// <summary>
/// A titled section that opens and closes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open" /> works exactly as <see cref="UiDropdown.Open" /> does, for the same reason: unset
/// is uncontrolled, and closed writes <c>collapse-close</c> rather than merely omitting
/// <c>collapse-open</c>, because daisyUI also opens on <c>:focus-within</c>.
/// </para>
/// <para>
/// This used to be a <c>&lt;details&gt;</c> with a <c>name</c>, which made a set of them mutually
/// exclusive with no state at all — the browser closed the others when one opened. What it could not do
/// is say WHICH one is open, so a page could neither restore that nor react to it. For a set that
/// behaves as one, <see cref="UiAccordion" /> holds the open key in C#; this is the standalone section.
/// </para>
/// </remarks>
public sealed partial class UiCollapse : Component
{
    /// <summary>daisyUI and MaryUI both call this <c>title</c>. <c>new</c> because the base type carries a
    /// markup entry of that name; this component renders no &lt;title&gt; element, so nothing is lost.</summary>
    public new required string Title { get; set; }

    /// <summary>Whether it is open. Leave it unset to let the browser open it on focus.</summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the heading is activated, with the state the reader is asking for.</summary>
    public Action<bool>? OnToggle { get; set; }

    /// <summary>Draws the arrow or plus marker.</summary>
    public UiMarker? Marker { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var title = Button
            .Type("button")
            .Class("collapse-title flex w-full items-center text-left font-semibold")
            .Aria(new Dictionary<string, string?> { ["expanded"] = Open == true ? "true" : "false" });

        if (OnToggle is { } toggle)
        {
            var next = Open != true;
            title = title.OnClick(() => toggle(next));
        }

        return Div.Class(UiClass.Compose(
            "collapse border border-base-300 bg-base-100",
            Marker is { } marker ? UiClassNames.Marker(marker) : "",
            Open switch { true => "collapse-open", false => "collapse-close", null => "" },
            Class))[
            title[Title],
            Div.Class("collapse-content text-sm")[Children ?? []]
        ];
    }
}
