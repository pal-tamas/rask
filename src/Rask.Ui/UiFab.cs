namespace Rask.Ui;

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
