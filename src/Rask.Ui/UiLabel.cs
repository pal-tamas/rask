namespace Rask.Ui;

/// <summary>
/// A caption attached to a control, inside the control's own frame.
/// </summary>
/// <remarks>
/// daisyUI's <c>label</c>, which draws the text as part of the field rather than above it — a currency
/// beside an amount, a unit beside a number, <c>https://</c> before a domain.
/// <b>It is decoration, not a name.</b> A <c>&lt;label&gt;</c> element names a control for assistive
/// technology; this styles text next to one. The control still needs its own name, which every kit
/// control takes as a required property.
/// </remarks>
public sealed partial class UiLabel : Component
{
    /// <summary>The text before the control.</summary>
    public new string? Text { get; set; }

    /// <summary>The text after it.</summary>
    public string? Trailing { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("label", Class))[
            Text is null ? null : Span[Text],
            Children ?? [],
            Trailing is null ? null : Span[Trailing]
        ];
}
