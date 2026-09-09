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
    /// <summary>
    ///     The alert's text. Optional: an alert can be written as a message, as children, or as both.
    /// </summary>
    /// <remarks>
    ///     Required once, on the reasoning that an alert with nothing to say is not an alert. Children
    ///     say it just as well, and demanding a message meant every alert carrying markup had to pass an
    ///     empty string to get past the requirement — which is the same requirement, satisfied
    ///     meaninglessly. Same change, and the same reason, as <c>UiButton.Label</c>.
    /// </remarks>
    public string? Message { get; set; }

    /// <summary><see cref="UiTone.Info" />, <see cref="UiTone.Success" />, <see cref="UiTone.Warning" /> or <see cref="UiTone.Error" />.</summary>
    public UiTone? Tone { get; set; }

    public UiVariant? Variant { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>
    ///     The element's <c>id</c>, for whatever needs to name it: a label's <c>for</c>, an
    ///     <c>aria-controls</c>, a fragment link, a test selector.
    /// </summary>
    public string? Id { get; set; }


    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Id(Id)
            .Role(Tone is UiTone.Error or UiTone.Warning ? "alert" : "status")
            .Class(UiClass.Compose(
                "alert",
                Tone is { } tone ? UiClassNames.AlertTone(tone) : "",
                Variant is { } variant ? UiClassNames.AlertVariant(variant) : "",
                Class))[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0") : null,
            Message is null ? null : Span[Message],
            Children ?? []
        ];
}
