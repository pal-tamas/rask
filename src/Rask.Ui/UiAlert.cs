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
    ///     What it says, when that is just text. Optional: an alert with richer content passes CHILDREN.
    /// </summary>
    /// <remarks>
    ///     It was required, and that is what kept rich alerts out of the kit — twenty-six of the showcase's
    ///     thirty-five carry an icon, a <c>&lt;strong&gt;</c> lead-in, a <c>&lt;code&gt;</c> span or an
    ///     exception message beside the text, and a required string has nowhere to put any of it. The
    ///     children were always rendered; only this stopped them being reachable on their own.
    /// </remarks>
    public string? Message { get; set; }

    /// <inheritdoc cref="UiButton.Id" />
    public string? Id { get; set; }

    /// <summary><see cref="UiTone.Info" />, <see cref="UiTone.Success" />, <see cref="UiTone.Warning" /> or <see cref="UiTone.Error" />.</summary>
    public UiTone? Tone { get; set; }

    public UiVariant? Variant { get; set; }

    public UiIconName? Icon { get; set; }

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
