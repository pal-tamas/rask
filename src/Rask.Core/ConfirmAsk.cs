namespace Rask.Core;

// The question `.Confirm(…)` asks, and the answer its prompt form waits for (null: a plain confirm).
internal sealed record ConfirmAsk(string Text, string? Expect);
