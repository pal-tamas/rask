namespace Rask.Core.Messaging;

/// <summary>The button a toast carries: what it says, and what pressing it does.</summary>
/// <param name="Label">The button's text — <c>"Undo"</c>.</param>
/// <param name="Run">What pressing it does; the toast is dismissed after.</param>
public sealed record ToastAction(string Label, Callback Run);
