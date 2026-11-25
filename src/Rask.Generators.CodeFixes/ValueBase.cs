using Microsoft.CodeAnalysis;

namespace Rask.Generators.CodeFixes;

// The Rask.Values base record RASK130's fix declares a value object from, by what the property holds, with the
// parameters of its constructor the fix may write, in their order (src/Rask.Core/Values).
internal sealed class ValueBase
{
    private static readonly ValueBase Text = new("Text", ["MaxLength", "MinLength", "Required"]);
    private static readonly ValueBase Number = new("Number", ["Min", "Max", "Scale", "Precision"]);
    private static readonly ValueBase Integer = new("Integer", ["Min", "Max"]);
    private static readonly ValueBase Day = new("Day", []);

    private ValueBase(string name, string[] parameters)
    {
        Name = name;
        Parameters = parameters;
    }

    public string Name { get; }

    public string[] Parameters { get; }

    public bool IsText => ReferenceEquals(this, Text);

    public bool IsInteger => ReferenceEquals(this, Integer);

    // A number base: its Min and Max are numbers.
    public bool IsNumeric => ReferenceEquals(this, Number) || IsInteger;

    // The base for a string, a decimal, an int or a long, or a DateOnly; null for anything else.
    public static ValueBase? Of(ITypeSymbol held) =>
        held.SpecialType switch
        {
            SpecialType.System_String => Text,
            SpecialType.System_Decimal => Number,
            SpecialType.System_Int32 or SpecialType.System_Int64 => Integer,
            _ => held is { Name: "DateOnly", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } ? Day : null,
        };
}
