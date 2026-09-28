namespace Rask.Generators.Translations;

// One placeholder in a message: the name a caller sees, the CLR type its parameter takes, and any
// .NET format specifier to apply.
internal sealed class Placeholder(string name, string clrType, string? format)
{
    public string Name { get; } = name;
    public string ClrType { get; } = clrType;
    public string? Format { get; } = format;
}
