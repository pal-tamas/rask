namespace Rask.Core;

// The question `.Confirm(…)` asks, the answer its prompt form waits for (null: a plain confirm), and the title the
// kit's dialog puts over it (null: the layout's).
internal sealed record ConfirmAsk(string Text, string? Expect, string? Heading);
