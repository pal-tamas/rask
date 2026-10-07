namespace Rask;

/// <summary>
///     How many lines a <see cref="UiTextarea{T}" /> shows: a number, or <see cref="Auto" /> to fit what is typed.
/// </summary>
/// <remarks><c>.Rows(2)</c> and <c>.Rows(UiTextareaRows.Auto)</c> — Flux's <c>rows="2"</c> and <c>rows="auto"</c>.</remarks>
public readonly record struct UiTextareaRows
{
    private UiTextareaRows(int? count) => Count = count;

    /// <summary>Grows with its content (<c>field-sizing: content</c>), starting one line tall.</summary>
    public static UiTextareaRows Auto { get; } = new(null);

    /// <summary>The fixed number of lines, or null for <see cref="Auto" />.</summary>
    public int? Count { get; }

    /// <summary>A fixed number of lines.</summary>
    public static UiTextareaRows FromInt32(int count) => new(count);

    /// <inheritdoc cref="FromInt32" />
    public static implicit operator UiTextareaRows(int count) => FromInt32(count);
}
