namespace Rask;

/// <summary>
/// A message in a conversation.
/// </summary>
/// <remarks>
/// <see cref="Mine" /> chooses the side. daisyUI has no notion of who is speaking, only of left and
/// right, so the component takes the meaningful question and answers the presentational one itself.
/// </remarks>
public sealed partial class UiChatBubble : Component
{
    public required string Message { get; set; }

    /// <summary>Who said it. Rendered above the bubble.</summary>
    public string? Author { get; set; }

    /// <summary>When. Rendered beside the author.</summary>
    public string? When { get; set; }

    /// <summary>Puts it on the trailing side, as the reader's own message.</summary>
    public bool? Mine { get; set; }

    public Ui.Tone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(Mine == true ? "chat chat-end" : "chat chat-start", Class))[
            Author is null && When is null
                ? null
                : Div.Class("chat-header")[
                    Author is { } author ? Span[author] : null,
                    When is { } when ? Time.Class("ms-1 text-xs opacity-50")[when] : null
                ],
            Div.Class(UiClass.Compose(
                "chat-bubble",
                Tone is { } tone ? ToneClass(tone) : ""))[Message]
        ];

    // A literal per tone, like every other class the kit writes: daisyUI emits a component's CSS only
    // where the scanner can see the whole name.
    private static string ToneClass(Ui.Tone tone) => tone switch
    {
        Ui.Tone.Primary => "chat-bubble-primary",
        Ui.Tone.Secondary => "chat-bubble-secondary",
        Ui.Tone.Accent => "chat-bubble-accent",
        Ui.Tone.Info => "chat-bubble-info",
        Ui.Tone.Success => "chat-bubble-success",
        Ui.Tone.Warning => "chat-bubble-warning",
        Ui.Tone.Error => "chat-bubble-error",
        Ui.Tone.Neutral => "chat-bubble-neutral",
        _ => "",
    };
}
