namespace Rask.Ui;

/// <summary>
/// Something the page needs to say, in place. It IS the <c>&lt;div&gt;</c>, and what it says is its children.
/// </summary>
/// <remarks>
/// <para>
/// <c>role="alert"</c> only when the tone is <see cref="UiTone.Error" /> or <see cref="UiTone.Warning" />:
/// that role interrupts a screen reader, which is right for a failure and rude for an explanation. The
/// rest announce politely as <c>status</c>. A role the call site sets wins.
/// </para>
/// <para>
/// A <see cref="UiElement" />, so an alert is <c>UiAlert.Tone(UiTone.Error)[UiIcon.Name(UiIconName.Warning),
/// "Payment failed"]</c> — the icon, a <c>&lt;strong&gt;</c> lead-in, a <c>&lt;code&gt;</c> span or an
/// exception message are all just children, which is what twenty-six of the showcase's thirty-five alerts
/// needed and a string <c>Message</c> had nowhere to put.
/// </para>
/// </remarks>
public sealed partial class UiAlert : UiElement
{
    /// <summary><see cref="UiTone.Info" />, <see cref="UiTone.Success" />, <see cref="UiTone.Warning" /> or <see cref="UiTone.Error" />.</summary>
    public UiTone? Tone { get; set; }

    public UiVariant? Variant { get; set; }

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "alert",
            Tone is { } tone ? UiClassNames.AlertTone(tone) : "",
            Variant is { } variant ? UiClassNames.AlertVariant(variant) : "",
            Class);

    /// <inheritdoc />
    private protected override string? ResolveRole() =>
        Role ?? (Tone is UiTone.Error or UiTone.Warning ? "alert" : "status");
}
