namespace Rask.Ui;

/// <summary>
/// A page with a panel that slides in beside it.
/// </summary>
/// <remarks>
/// <para>
/// The panel's state lives in a checkbox, and it stays there on purpose — this is the one interactive
/// component in the kit that did not move its state into C# wholesale. daisyUI's rules are written
/// against <c>.drawer-toggle:checked</c> and <c>.drawer-open &gt; .drawer-toggle</c>, so the input is
/// not an implementation detail the kit could swap out; removing it removes the component.
/// </para>
/// <para>
/// What C# gets instead is the same state, both ways. <see cref="Open" /> sets the checkbox and
/// <see cref="OnToggle" /> reports it changing, so a page can close the drawer when a navigation
/// completes and can restore it on the next render — the two things the checkbox alone could not do.
/// Leave both unset and it behaves exactly as before, sliding with no runtime attached.
/// </para>
/// <para>
/// The button that opens it is a <c>&lt;label&gt;</c> pointing at the checkbox, which is why
/// <see cref="Id" /> is required and has to be unique on the page. The overlay is a label too, so a
/// click outside closes it — the one dismissal a <see cref="UiDropdown" /> cannot offer without script.
/// Pair it with <c>lg:drawer-open</c> in <see cref="Class" /> to have the panel simply be there on a
/// wide screen; that is what daisyUI's <c>drawer-open</c> is for, and it is a layout choice rather than
/// a state, so it stays a class rather than becoming a property.
/// </para>
/// </remarks>
public sealed partial class UiDrawer : Component
{
    /// <summary>Joins the toggle, the overlay and the panel. Must be unique on the page.</summary>
    public required string Id { get; set; }

    /// <summary>The panel's contents. The page itself is this component's children.</summary>
    public required Component Side { get; set; }

    /// <summary>Whether the panel is showing. Unset leaves the state to the checkbox alone.</summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the panel is opened or closed, with the state being asked for.</summary>
    public Callback<bool>? OnToggle { get; set; }

    /// <summary>The accessible name for the closing overlay.</summary>
    public string? CloseLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var toggle = Input.Of<bool>().Checked(Open == true).Id(Id).Class("drawer-toggle");

        if (OnToggle is { } onToggle)
        {
            toggle = toggle.OnChange(onToggle);
        }

        return Div.Class(UiClass.Compose("drawer", Class))[
            toggle,
            Div.Class("drawer-content")[Children ?? []],
            Div.Class("drawer-side")[
                Label
                    .For(Id)
                    .Class("drawer-overlay")
                    .Aria(new Dictionary<string, string?> { ["label"] = CloseLabel ?? "Close" }),
                Side
            ]
        ];
    }
}
