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

    /// <summary>What the block is — <c>"Payload"</c>, <c>"Last error"</c> — set small above it.</summary>
    /// <remarks>Coloured with the block, so an error's caption reads as part of the error.</remarks>
    public string? Label { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var failed = Tone == UiTone.Error;
        Component block = Pre.Class(
            "max-h-72 overflow-auto whitespace-pre-wrap break-all rounded-lg border border-base-300 bg-base-200 "
            + "p-3 font-mono text-xs " + (failed ? "text-error" : "opacity-60"))[
            Content
        ];

        return Label is null
            ? block
            : Div[
                Div.Class("mb-1.5 text-xs font-medium " + (failed ? "text-error" : "opacity-60"))[Label],
                block
            ];
    }
}
