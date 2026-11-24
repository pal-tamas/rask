namespace Rask.Values;

/// <summary>What a <see cref="Text" /> was handed.</summary>
/// <param name="MaxLength">The most characters; 0 for no limit.</param>
/// <param name="MinLength">The fewest characters; 0 for none beyond being filled.</param>
/// <param name="Pattern">The pattern the text must match, or null.</param>
/// <param name="Required">Whether it must be filled; an empty text is a value of its own when not.</param>
internal sealed record TextRules(int MaxLength, int MinLength, string? Pattern, bool Required = true) : IValueRules;
