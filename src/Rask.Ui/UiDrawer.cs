namespace Rask.Ui;

/// <summary>
/// A page with a panel that slides in beside it.
/// </summary>
/// <remarks>
/// <para>
/// The open state is a hidden checkbox and the sliding is CSS, so this works with no script: the button
/// that opens it is a <c>&lt;label&gt;</c> pointing at the checkbox, which is why <see cref="Id" /> is
/// required and has to be unique on the page.
/// </para>
/// <para>
/// The overlay is a label too, so a click outside closes it — the one dismissal a
/// <see cref="UiDropdown" /> cannot offer without script. Pair it with <c>lg:drawer-open</c> in
/// <see cref="Class" /> to have the panel simply be there on a wide screen.
/// </para>
/// </remarks>
public sealed partial class UiDrawer : Component
{
    /// <summary>Joins the toggle, the overlay and the panel. Must be unique on the page.</summary>
    public required string Id { get; set; }

    /// <summary>The panel's contents. The page itself is this component's children.</summary>
    public required Component Side { get; set; }

    /// <summary>The accessible name for the closing overlay.</summary>
    public string? CloseLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("drawer", Class))[
            Input.Value(false).Id(Id).Class("drawer-toggle"),
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
