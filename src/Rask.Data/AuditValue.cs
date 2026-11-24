namespace Rask.Data;

/// <summary>
///     One value an <see cref="AuditEntry" /> records: the column's path — <c>Number</c>, <c>Km.Start</c>,
///     <c>Lines[1].Quantity</c> — and the value as it is stored, or null.
/// </summary>
/// <param name="Name">The path to the value.</param>
/// <param name="Value">The value as the database stores it (a value object's provider value), or null.</param>
public readonly record struct AuditValue(string Name, object? Value);
