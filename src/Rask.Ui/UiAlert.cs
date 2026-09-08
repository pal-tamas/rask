namespace Rask.Ui;

/// <summary>
/// Something the page needs to say, in place.
/// </summary>
/// <remarks>
/// <c>role="alert"</c> only when the tone is <see cref="UiTone.Error" /> or <see cref="UiTone.Warning" />:
/// that role interrupts a screen reader, which is right for a failure and rude for an explanation. The
/// rest announce politely as <c>status</c>.
/// </remarks>
public sealed partial class UiAlert : Component
{
    public required string Message { get; set; }

    /// <summary><see cref="UiTone.Info" />, <see cref="UiTone.Success" />, <see cref="UiTone.Warning" /> or <see cref="UiTone.Error" />.</summary>
    public UiTone? Tone { get; set; }

    public UiVariant? Variant { get; set; }

    public UiIconName? Icon { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Role(Tone is UiTone.Error or UiTone.Warning ? "alert" : "status")
            .Class(UiClass.Compose(
                "alert",
                Tone is { } tone ? UiClassNames.AlertTone(tone) : "",
                Variant is { } variant ? UiClassNames.AlertVariant(variant) : "",
                Class))[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0") : null,
            Span[Message],
            Children ?? []
        ];
}
