namespace Rask.Ui;

/// <summary>
/// A block of machine output — a stack trace, a stored payload.
/// </summary>
/// <remarks>
/// Scrolls on its own and wraps on overflow, because the alternative is a 400-character exception message
/// making the whole page scroll sideways. <see cref="UiTone.Error" /> for anything that is the reason
/// something failed.
/// </remarks>
public sealed partial class UiCode : Component
{
    // Not `Text`: that name is the Text component's builder entry, inherited from Component (CS0108).
    public required string Content { get; set; }

    /// <summary><see cref="UiTone.Error" /> to read it as a failure. Anything else is neutral machine output.</summary>
    public UiTone? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Pre.Class(
            "max-h-72 overflow-auto whitespace-pre-wrap break-all rounded-lg border border-base-300 bg-base-200 "
            + "p-3 font-mono text-xs " + (Tone == UiTone.Error ? "text-error" : "opacity-60"))[
            Content
        ];
}
