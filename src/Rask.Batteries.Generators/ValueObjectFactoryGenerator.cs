using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Writes the generated half of a value object declared from a base record —
/// <c>public sealed partial record DriverName() : Text(MaxLength: 1000);</c>: the private constructor EF Core rebuilds
/// a stored value through, <c>Create</c>, <c>TryCreate</c> and <c>CreateOptional</c>, and the <c>__Problems</c> a form
/// and a generated write ask.
/// </summary>
/// <remarks>
/// The base keeps the rules and says which one a value broke; the messages are worded here, from Rask's strings
/// catalog where the compilation has Rask.Core, so they are in the reader's language — the server build of
/// Rask.Data cannot reach the catalog itself.
/// </remarks>
[Generator]
public sealed class ValueObjectFactoryGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Rask129 = new(
        "RASK129",
        "A value object declared from a base record is not 'sealed partial'",
        "'{0}' is declared from Rask.Data.{1}, so it must be {2} — Rask writes its other half",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A record deriving from Text, Number, Whole or Day gets its constructor, Create, TryCreate and "
                     + "CreateOptional generated into it, so it has to be partial; and it is the value itself, never a "
                     + "base for another, so it has to be sealed. A type it is nested in has to be partial too.",
        helpLinkUri: DiagnosticHelp.Link("RASK129"));

    private const string Strings = "global::Rask.Core.Globalization.RaskStrings";
    private const string Keys = "global::Rask.Core.Globalization.RaskString";
    private const string Kinds = "global::Rask.Data.ValueProblemKind";
    private const string Rules = "global::Rask.Data.GeneratedValueRules";
    private const string Messages = "global::System.Collections.Generic.IReadOnlyList<string>";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var values = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, token) => Describe(ctx, token))
            .Where(static value => value is not null)
            .Select(static (value, _) => value!);

        // Worded from the catalog only where the compilation has one with every key the rules name.
        var catalog = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Rask.Core.Globalization.RaskStrings") is not null &&
            compilation.GetTypeByMetadataName("Rask.Core.Globalization.RaskString")?.GetMembers("ValidationDecimals").Length > 0);

        context.RegisterSourceOutput(values.Collect().Combine(catalog), static (spc, all) => Emit(spc, all.Left, all.Right));
    }

    private static ValueObject? Describe(GeneratorSyntaxContext ctx, CancellationToken token)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((RecordDeclarationSyntax)ctx.Node, token) is not INamedTypeSymbol type ||
            AggregateShape.ValueBaseOf(type) is not { } valueBase)
        {
            return null;
        }

        var containers = new List<string>();
        var problem = Lacks(type);
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (!IsPartial(outer))
            {
                problem ??= "partial, and so must '" + outer.Name + "', which it is nested in";
            }

            containers.Insert(0, "partial " + Keyword(outer) + " " + outer.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        }

        return new ValueObject(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.Name,
            type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            valueBase,
            new EquatableArray<string>([.. containers]),
            type.IsGenericType,
            problem,
            SymbolLocation.From(type));
    }

    // What the declaration lacks, in the words of the message: "sealed partial", "sealed" or "partial".
    private static string? Lacks(INamedTypeSymbol type)
    {
        var isPartial = IsPartial(type);
        if (type.IsSealed && isPartial)
        {
            return null;
        }

        if (type.IsSealed)
        {
            return "partial";
        }

        return isPartial ? "sealed" : "sealed partial";
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Length > 0 &&
        type.DeclaringSyntaxReferences.All(static r =>
            r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string Keyword(INamedTypeSymbol type) =>
        type switch
        {
            { IsRecord: true, IsValueType: true } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class",
        };

    private static void Emit(SourceProductionContext context, ImmutableArray<ValueObject> all, bool catalog)
    {
        // Distinct because a partial record contributes one entry per declaration.
        foreach (var value in all.Distinct().OrderBy(static v => v.FullyQualifiedName, System.StringComparer.Ordinal))
        {
            if (value.Problem is { } problem)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask129, value.Location?.ToLocation(), value.Name, value.ValueBase, "'" + problem + "'"));
                continue;
            }

            if (value.Generic)
            {
                continue;
            }

            context.AddSource(
                value.FullyQualifiedName.Replace("global::", "").Replace('<', '_').Replace('>', '_') + ".Value.g.cs",
                SourceText.From(Render(value, catalog), Encoding.UTF8));
        }
    }

    private static string Render(ValueObject value, bool catalog)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (value.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(value.Namespace).AppendLine(";");
            s.AppendLine();
        }

        foreach (var container in value.Containers)
        {
            s.AppendLine(container);
            s.AppendLine("{");
        }

        s.Append("partial record ").AppendLine(value.Name);
        s.AppendLine("{");
        AppendMembers(s, value, Held(value.ValueBase));
        AppendSay(s, value.ValueBase, catalog);
        s.AppendLine("}");

        foreach (var _ in value.Containers)
        {
            s.AppendLine("}");
        }

        return s.ToString();
    }

    private static string Held(string valueBase) =>
        valueBase switch
        {
            "Text" => "string",
            "Number" => "decimal",
            "Whole" => "long",
            _ => "global::System.DateOnly",
        };

    private static void AppendMembers(StringBuilder s, ValueObject value, string held)
    {
        var name = value.Name;
        var text = string.Equals(held, "string", System.StringComparison.Ordinal);
        var given = text ? "value!" : "value.GetValueOrDefault()";
        var blank = text ? "string.IsNullOrWhiteSpace(value)" : "value is null";

        s.Append("    private static readonly ").Append(name).AppendLine(" __Rules = new();");
        s.AppendLine();
        s.AppendLine("    // How EF Core rebuilds a stored value: as it was stored, never checked again.");
        s.Append("    private ").Append(name).Append('(').Append(held).AppendLine(" value) : this() => Value = value;");
        s.AppendLine();
        s.Append("    /// <summary>A <see cref=\"").Append(name).AppendLine("\" /> holding <paramref name=\"value\" />, once its rules accept it.</summary>");
        s.AppendLine("    /// <param name=\"value\">The value as given.</param>");
        s.AppendLine("    /// <param name=\"field\">The field it came from, as a form names it, so a refusal is shown under it.</param>");
        s.AppendLine("    /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">The rules refuse it.</exception>");
        s.Append("    public static ").Append(name).Append(" Create(").Append(held).AppendLine("? value, string? field = null)");
        s.AppendLine("    {");
        s.AppendLine("        var problems = __Problems(value);");
        s.Append("        return problems.Count == 0 ? new ").Append(name).Append("(__Rules.Normalize(").Append(given)
            .Append(")) : throw ").Append(Rules).AppendLine(".Refused(problems, field);");
        s.AppendLine("    }");
        s.AppendLine();
        s.Append("    /// <summary>A <see cref=\"").Append(name).AppendLine("\" /> holding <paramref name=\"value\" />, or what its rules say against it.</summary>");
        s.Append("    public static bool TryCreate(").Append(held).Append("? value, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ")
            .Append(name).Append("? result, out ").Append(Messages).AppendLine(" problems)");
        s.AppendLine("    {");
        s.AppendLine("        problems = __Problems(value);");
        s.Append("        result = problems.Count == 0 ? new ").Append(name).Append("(__Rules.Normalize(").Append(given).AppendLine(")) : null;");
        s.AppendLine("        return result is not null;");
        s.AppendLine("    }");
        s.AppendLine();
        s.Append("    /// <summary>Nothing for a value left ").Append(text ? "blank" : "empty").AppendLine("; otherwise <see cref=\"Create\" />.</summary>");
        s.Append("    public static ").Append(name).Append("? CreateOptional(").Append(held).AppendLine("? value, string? field = null) =>");
        s.Append("        ").Append(blank).AppendLine(" ? null : Create(value, field);");
        s.AppendLine();
        s.AppendLine("    /// <summary>What the rules say against <paramref name=\"value\" />: what a form and a generated write ask.</summary>");
        s.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        s.Append("    public static ").Append(Messages).Append(" __Problems(").Append(held).AppendLine("? value) =>");
        s.Append("        ").Append(Rules).AppendLine(".Said(__Rules.Problems(value), __Say);");

        if (text)
        {
            s.AppendLine();
            s.AppendLine("    /// <summary>The most characters it may hold, or null for no limit: what a bound input is given as its maxlength.</summary>");
            s.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            s.AppendLine("    public static int? __MaxLength => __Rules.MaxLength > 0 ? __Rules.MaxLength : null;");
        }
    }

    // Each rule the base can break, worded from the catalog; the English stands in where there is none.
    private static void AppendSay(StringBuilder s, string valueBase, bool catalog)
    {
        s.AppendLine();
        if (!catalog)
        {
            s.AppendLine("    private static readonly global::System.Func<global::Rask.Data.ValueProblem, string>? __Say = null;");
            return;
        }

        s.AppendLine("    private static string __Say(global::Rask.Data.ValueProblem problem) =>");
        s.AppendLine("        problem.Kind switch");
        s.AppendLine("        {");
        foreach (var (kind, key, limit) in Sentences(valueBase))
        {
            s.Append("            ").Append(Kinds).Append('.').Append(kind).Append(" => ").Append(Strings).Append(".Get(")
                .Append(Keys).Append('.').Append(key).Append(", problem.Template").Append(limit ? ", problem.Limit" : "").AppendLine("),");
        }

        s.AppendLine("            _ => problem.English,");
        s.AppendLine("        };");
    }

    private static IEnumerable<(string Kind, string Key, bool Limit)> Sentences(string valueBase)
    {
        yield return ("Required", "ValidationRequired", false);

        switch (valueBase)
        {
            case "Text":
                yield return ("TooShort", "ValidationTooShort", true);
                yield return ("TooLong", "ValidationTooLong", true);
                yield return ("Pattern", "ValidationPattern", false);
                break;
            case "Day":
                yield return ("BeforeEarliest", "ValidationDateMin", true);
                yield return ("AfterLatest", "ValidationDateMax", true);
                break;
            default:
                yield return ("BelowMin", "ValidationNumberMin", true);
                yield return ("AboveMax", "ValidationNumberMax", true);
                if (string.Equals(valueBase, "Number", System.StringComparison.Ordinal))
                {
                    yield return ("TooManyDecimals", "ValidationDecimals", true);
                    yield return ("TooManyDigits", "ValidationNumberRange", false);
                }

                break;
        }
    }

    private sealed record ValueObject(
        string FullyQualifiedName,
        string Name,
        string Namespace,
        string ValueBase,
        EquatableArray<string> Containers,
        bool Generic,
        string? Problem,
        SymbolLocation? Location);
}
