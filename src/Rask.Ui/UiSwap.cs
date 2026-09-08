namespace Rask.Ui;

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
