using Rask.Cqrs;

namespace Rask.Data;

/// <summary>A row <c>X.Import</c> set aside: where it stood, what it was, and why it was refused.</summary>
/// <typeparam name="TRow">The rows imported.</typeparam>
/// <param name="Index">Its position among the rows given, from 0 — the line a reader is told to fix is this plus the header.</param>
/// <param name="Row">The row as given.</param>
/// <param name="Refusal">What refused it: its factory, a value object's rules, the aggregate's <c>Validate()</c>, a unique index.</param>
public sealed record RefusedRow<TRow>(int Index, TRow Row, RaskValidationException Refusal);
