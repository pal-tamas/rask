namespace Rask;

/// <summary>
/// Something the page needs to say, in place. It IS the <c>&lt;div&gt;</c>, and what it says is its children.
/// </summary>
/// <remarks>
/// <para>
/// <c>role="alert"</c> only when the tone is <see cref="Ui.Tone.Error" /> or <see cref="Ui.Tone.Warning" />:
/// that role interrupts a screen reader, which is right for a failure and rude for an explanation. The
/// rest announce politely as <c>status</c>. A role the call site sets wins.
/// </para>
/// <para>
/// A <see cref="UiElement" />, so an alert is <c>Ui.Alert.Tone(Ui.Tone.Error)[Ui.Icon.Name(Ui.IconName.Warning),
/// "Payment failed"]</c> — the icon, a <c>&lt;strong&gt;</c> lead-in, a <c>&lt;code&gt;</c> span or an
/// exception message are all just children, which is what twenty-six of the showcase's thirty-five alerts
/// needed and a string <c>Message</c> had nowhere to put.
/// </para>
/// </remarks>
public sealed partial class UiAlert : UiElement
{
    /// <summary><see cref="Ui.Tone.Info" />, <see cref="Ui.Tone.Success" />, <see cref="Ui.Tone.Warning" /> or <see cref="Ui.Tone.Error" />.</summary>
    public Ui.Tone? Tone { get; set; }

    public Ui.Variant? Variant { get; set; }

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
        Role ?? (Tone is Ui.Tone.Error or Ui.Tone.Warning ? "alert" : "status");
}
