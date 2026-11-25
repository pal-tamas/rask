namespace Rask;

/// <summary>When a bound field's control writes the model: its <c>Live</c>, <c>Blur</c>, <c>Debounce</c> and <c>Defer</c>.</summary>
/// <param name="Live">Writes as the reader types or picks.</param>
/// <param name="Blur">Writes when the reader leaves the field: a text field's only.</param>
/// <param name="Debounce">Writes once typing has paused this long: a text field's only.</param>
/// <param name="Defer">Waits for the next action even in a form over an operation: a text field's only.</param>
internal readonly record struct UiBoundTiming(bool? Live, bool? Blur, TimeSpan? Debounce, bool? Defer);
