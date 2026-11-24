namespace Rask.Values;

/// <summary>What a value object declared from <see cref="Text" /> or one of its kinds hands its base.</summary>
/// <param name="MaxLength">The most characters; 0 for no limit.</param>
/// <param name="MinLength">The fewest characters; 0 for none beyond being filled.</param>
/// <param name="Pattern">The pattern the text must match, or null.</param>
/// <param name="Message">What a text the pattern or the kind's format refuses is told, or null for the framework's.</param>
/// <param name="Trim">Whether surrounding blanks are cut off.</param>
/// <param name="Upper">Whether the text is kept in capitals.</param>
/// <param name="Required">Whether it must be filled.</param>
/// <param name="Kind">The kind of text: what its format is checked against and what a bound input is typed into.</param>
internal readonly record struct TextDeclared(
    int MaxLength,
    int MinLength,
    string? Pattern,
    string? Message,
    bool Trim,
    bool Upper,
    bool Required,
    TextKind Kind = TextKind.Plain);
