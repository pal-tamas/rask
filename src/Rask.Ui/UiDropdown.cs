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
    public Callback<bool>? OnToggle { get; set; }

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
            trigger = trigger.OnClick(() => toggle.Invoke(next) ?? Task.CompletedTask);
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
