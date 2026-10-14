namespace Rask.Core;

// The question `.Confirm(…)` asks, and the answer its prompt form waits for (null: a plain confirm).
// Written: a chain step named it since the end of the last render (Element.ResetUnwrittenConfirm clears it).
internal sealed class ConfirmAsk(string text, string? expect)
{
    public string Text { get; } = text;

    public string? Expect { get; } = expect;

    public bool Written { get; set; }
}
