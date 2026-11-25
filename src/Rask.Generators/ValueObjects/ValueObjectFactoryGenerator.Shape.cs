using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators;

// The field shape a value object declares — `__Field`, what a control bound to it is told (Rask.Core.Forms.FieldShape):
// the kind of value it holds and the rules its base call states as constants. A bound that is not a constant — a
// Day's NotBefore, a DateOnly — is not read, and the rule is still checked when the value is made.
public sealed partial class ValueObjectFactoryGenerator
{
    private const string FieldShapeType = "global::Rask.Core.Forms.FieldShape";
    private const string FieldKindType = "global::Rask.Core.Forms.FieldKind";

    /// <summary>The rules a value object's base call states as constants, as a field shape names them; null where none.</summary>
    private sealed record ShapeFacets(
        int? MaxLength = null,
        int? MinLength = null,
        decimal? Min = null,
        decimal? Max = null,
        int? Scale = null,
        bool NotAfterToday = false,
        bool NotAfterNow = false,
        bool Strict = false,
        string? Start = null,
        string? End = null,
        string? Ends = null,
        bool Upper = false,
        bool New = false,
        string? Pattern = null,
        bool Trim = false);

    private static ShapeFacets ReadShape(SemanticModel model, RecordDeclarationSyntax node, CancellationToken token)
    {
        var shape = RangeEnds(model, node, token);
        foreach (var (name, value) in BaseArguments(model, node, token))
        {
            shape = WithFacet(shape, name, value);
        }

        return shape;
    }

    // A range's ends: the record's first two primary-constructor parameters, by name and by the kind of their type.
    private static ShapeFacets RangeEnds(SemanticModel model, RecordDeclarationSyntax node, CancellationToken token)
    {
        if (node.ParameterList is not { Parameters.Count: >= 2 } list ||
            model.GetDeclaredSymbol(list.Parameters[0], token) is not IParameterSymbol start ||
            model.GetDeclaredSymbol(list.Parameters[1], token) is not IParameterSymbol end)
        {
            return new ShapeFacets();
        }

        return new ShapeFacets(Start: start.Name, End: end.Name, Ends: KindOfEnd(start.Type));
    }

    // Integer for an integer, Number for a fraction, Day for a DateOnly; null for any other end — a moment, a time.
    private static string? KindOfEnd(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        return held.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 => "Integer",
            SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single => "Number",
            _ => held.ToDisplayString() is "System.DateOnly" ? "Day" : null,
        };
    }

    private static ShapeFacets WithFacet(ShapeFacets shape, string? name, object value) =>
        (name, value) switch
        {
            ("MaxLength", int most) when most > 0 => shape with { MaxLength = most },
            ("MinLength", int least) when least > 0 => shape with { MinLength = least },
            ("Scale", int scale) => shape with { Scale = scale },
            ("NotAfterToday", bool notAfter) => shape with { NotAfterToday = notAfter },
            ("NotAfterNow", bool notAfter) => shape with { NotAfterNow = notAfter },
            ("Strict", bool strict) => shape with { Strict = strict },
            ("Upper", bool upper) => shape with { Upper = upper },
            ("Pattern", string pattern) => shape with { Pattern = pattern },
            ("Trim", bool trim) => shape with { Trim = trim },
            ("New", bool chosen) => shape with { New = chosen },
            ("Min", _) when AsDecimal(value) is { } min => shape with { Min = min },
            ("Max", _) when AsDecimal(value) is { } max => shape with { Max = max },
            _ => shape,
        };

    private static decimal? AsDecimal(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or double or float
            ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            : null;

    // `__Field`: only for the bases a control is drawn for, and only where the compilation has the seam to name.
    private static void AppendField(StringBuilder s, ValueObject value)
    {
        if (!HasField(value))
        {
            return;
        }

        var facets = value.Shape;
        var named = new List<string> { FieldKindType + "." + value.ValueBase };
        AddFacet(named, "MaxLength", facets.MaxLength);
        AddFacet(named, "MinLength", facets.MinLength);
        AddDecimal(named, "Min", facets.Min);
        AddDecimal(named, "Max", facets.Max);
        AddFacet(named, "Scale", facets.Scale);
        if (facets.NotAfterToday)
        {
            named.Add("NotAfterToday: true");
        }

        if (facets.NotAfterNow)
        {
            named.Add("NotAfterNow: true");
        }

        if (string.Equals(value.ValueBase, "Range", StringComparison.Ordinal))
        {
            AddRangeEnds(named, facets);
        }

        s.AppendLine();
        s.AppendLine("    /// <summary>What a control bound to it is told: the kind of value it holds and the rules it declares.</summary>");
        s.AppendLine(Hidden);
        // With what its own static Rules (FluentValidation) state for its value, the tightest of both; a kind of text
        // adds what its input is told (Told).
        var init = Told(value.TextKind, facets);
        s.Append("    public static ").Append(FieldShapeType).Append(" __Field => global::Rask.Core.Forms.StatedRules.Shape(typeof(")
            .Append(value.Name).Append("), \"Value\", new ").Append(FieldShapeType).Append('(').Append(string.Join(", ", named))
            .AppendLine(init.Count > 0 ? ") { " + string.Join(", ", init) + " });" : "));");
    }

    // What a kind of text tells its input, as the attribute it stands for does ([EmailAddress] an email input), a
    // text kept in capitals that it is, and any other the pattern it must match.
    private static List<string> Told(string? kind, ShapeFacets facets)
    {
        const string Input = "Input = global::Rask.Core.InputType.";
        var init = kind switch
        {
            "Email" => new List<string> { Input + "Email", "AutoComplete = \"email\"" },
            "Phone" => [Input + "Tel", "AutoComplete = \"tel\"", "InputMode = global::Rask.Core.InputMode.Tel"],
            "Url" => [Input + "Url"],
            "Password" => [Input + "Password", facets.New ? "AutoComplete = \"new-password\"" : "AutoComplete = \"current-password\""],
            "Multiline" => ["Hint = global::Rask.Core.Forms.FieldHints.MultilineText"],
            _ => [],
        };

        if (facets.Upper)
        {
            init.Add("Upper = true");
        }
        else if (facets.Pattern is { } pattern)
        {
            // A pattern the text must contain, as the whole-text one an input is told (HtmlPattern.Found); a text kept in
            // capitals is matched once it is, so what is typed in small letters would be refused by the browser alone.
            init.Add("Pattern = global::Rask.Core.Forms.HtmlPattern.Found(" + SymbolDisplay.FormatLiteral(pattern, quote: true) + ", trimmed: "
                     + (facets.Trim ? "true" : "false") + ")");
        }

        return init;
    }

    private static void AddRangeEnds(List<string> named, ShapeFacets facets)
    {
        if (facets.Strict)
        {
            named.Add("Strict: true");
        }

        if (facets.Start is { } start && facets.End is { } end)
        {
            named.Add("Start: \"" + start + "\"");
            named.Add("End: \"" + end + "\"");
        }

        if (facets.Ends is { } ends)
        {
            named.Add("Ends: " + FieldKindType + "." + ends);
        }
    }

    private static void AddDecimal(List<string> named, string name, decimal? value)
    {
        if (value is { } given)
        {
            named.Add(name + ": " + given.ToString(CultureInfo.InvariantCulture) + "m");
        }
    }
}
