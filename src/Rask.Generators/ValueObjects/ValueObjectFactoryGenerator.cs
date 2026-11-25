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

namespace Rask.Generators;

/// <summary>
/// Writes the generated half of a value object declared from a Rask.Values base record —
/// <c>public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);</c>: <c>Create</c>,
/// <c>TryCreate</c> and <c>CreateOptional</c> over its primary constructor's parameters, and for one holding one value
/// the <c>__Problems</c> a form and a generated write ask, plus the column facets (<c>__Facets</c>) and the field shape a
/// bound control is told (<c>__Field</c>), read from the arguments it hands its base.
/// </summary>
/// <remarks>
/// <para>
/// No rule is written here: every member makes the value through its constructor, which checks them, and reads the
/// answer — inside an attempt (<c>GeneratedValueRules.Attempt</c>) for the ones that must not throw.
/// </para>
/// </remarks>
[Generator]
public sealed partial class ValueObjectFactoryGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Rask129 = new(
        "RASK129",
        "A value object declared from a base record is not 'sealed partial'",
        "'{0}' is declared from Rask.Values.{1}, so it must be {2} — Rask writes its other half",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A record deriving from Value, Text, Number, Integer, Day, Moment or Range gets Create, TryCreate and "
                     + "CreateOptional generated into it, so it has to be partial; and it is the value itself, never a "
                     + "base for another, so it has to be sealed. A type it is nested in has to be partial too.",
        helpLinkUri: DiagnosticHelp.Link("RASK129"));

    private const string Rules = "global::Rask.Values.GeneratedValueRules";
    private const string ProblemType = "global::Rask.Values.Problem";
    private const string ProblemList = "global::System.Collections.Generic.IReadOnlyList<" + ProblemType + ">";
    private const string Stored = "global::Rask.Values.IStoredValue";
    private const string Messages = "global::System.Collections.Generic.IReadOnlyList<string>";
    private const string Hidden = "    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var values = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, token) => Describe(ctx, token))
            .Where(static value => value is not null)
            .Select(static (value, _) => value!);

        context.RegisterSourceOutput(values.Collect(), static (spc, all) => Emit(spc, all));

    }

    private static ValueObject? Describe(GeneratorSyntaxContext ctx, CancellationToken token)
    {
        var node = (RecordDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(node, token) is not INamedTypeSymbol type ||
            AggregateShape.DeclaredValueBaseOf(type) is not { } valueBase ||
            IsFrameworkBase(type) ||
            !IsPrimaryDeclaration(type, node))
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
            AggregateShape.TextKindOf(type),
            new EquatableArray<string>([.. containers]),
            type.IsGenericType,
            ReadParameters(ctx.SemanticModel, node, token),
            ReadFacets(ctx.SemanticModel, node, token),
            ReadShape(ctx.SemanticModel, node, token),
            ctx.SemanticModel.Compilation.GetTypeByMetadataName("Rask.Core.Forms.FieldShape") is not null,
            DeclaresToString(type),
            IsOrdered(type, node.ParameterList, ctx.SemanticModel, token),
            problem,
            SymbolLocation.From(type));
    }

    // The bases themselves — Text, Email, Range in Rask.Values — are declared from Value too, and are abstract on
    // purpose: this generator runs on Rask.Core as well, and writes nothing for them.
    private static bool IsFrameworkBase(INamedTypeSymbol type) =>
        type.IsAbstract &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Values", System.StringComparison.Ordinal);

    // One declaration speaks for a partial record: the one with the primary constructor, else the first.
    private static bool IsPrimaryDeclaration(INamedTypeSymbol type, RecordDeclarationSyntax node)
    {
        var declarations = type.DeclaringSyntaxReferences.Select(static r => r.GetSyntax()).OfType<RecordDeclarationSyntax>().ToList();
        var primary = declarations.Find(static d => d.ParameterList is not null) ?? declarations.FirstOrDefault();
        return primary is not null && primary.SyntaxTree == node.SyntaxTree && primary.Span == node.Span;
    }

    private static EquatableArray<Parameter> ReadParameters(SemanticModel model, RecordDeclarationSyntax node, CancellationToken token)
    {
        if (node.ParameterList is not { } list)
        {
            return new EquatableArray<Parameter>([]);
        }

        var parameters = new List<Parameter>();
        foreach (var syntax in list.Parameters)
        {
            if (model.GetDeclaredSymbol(syntax, token) is not { } parameter)
            {
                continue;
            }

            var declared = parameter.Type;
            var nullable = declared.NullableAnnotation == NullableAnnotation.Annotated ||
                           declared.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            parameters.Add(new Parameter(
                parameter.Name,
                declared.ToDisplayString(TypeFormat),
                NullableForm(declared, nullable),
                declared.SpecialType == SpecialType.System_String,
                declared.IsValueType,
                nullable,
                IsFormattable(declared),
                IsoFormat(declared)));
        }

        return new EquatableArray<Parameter>([.. parameters]);
    }

    // The type CreateOptional takes the parameter as: itself when it may already be null, else its nullable form.
    private static string NullableForm(ITypeSymbol declared, bool nullable)
    {
        if (nullable)
        {
            return declared.ToDisplayString(TypeFormat);
        }

        return declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat) + "?";
    }

    // The column facets the record hands its base — `Text(Value, MaxLength: Limits.Name)` — as constants, read
    // here so the column is shaped without building an instance (which would check its rules).
    private static ColumnFacets ReadFacets(SemanticModel model, RecordDeclarationSyntax node, CancellationToken token)
    {
        int? maxLength = null, precision = null, scale = null;
        foreach (var (name, constant) in BaseArguments(model, node, token))
        {
            if (constant is not int value)
            {
                continue;
            }

            switch (name)
            {
                case "MaxLength" when value > 0:
                    maxLength = value;
                    break;
                case "Precision":
                    precision = value;
                    break;
                case "Scale":
                    scale = value;
                    break;
            }
        }

        return new ColumnFacets(maxLength, precision, scale);
    }

    // What the record hands its base, as constants by parameter name: the base constructor's own defaults first —
    // `Email(Value)` is 254 characters at most without saying so — then each argument the record writes, which wins.
    private static List<(string Name, object Value)> BaseArguments(SemanticModel model, RecordDeclarationSyntax node, CancellationToken token)
    {
        var call = node.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();
        if (call is null || model.GetSymbolInfo(call, token).Symbol is not IMethodSymbol constructor)
        {
            return [];
        }

        var given = new Dictionary<string, object>(System.StringComparer.Ordinal);
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: { } fallback })
            {
                given[parameter.Name] = fallback;
            }
        }

        for (var i = 0; i < call.ArgumentList.Arguments.Count; i++)
        {
            var argument = call.ArgumentList.Arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText ??
                       (i < constructor.Parameters.Length ? constructor.Parameters[i].Name : null);
            var constant = model.GetConstantValue(argument.Expression, token);
            if (name is null)
            {
                continue;
            }

            if (constant is { HasValue: true, Value: { } value })
            {
                given[name] = value;
            }
            else
            {
                given.Remove(name);
            }
        }

        return [.. given.Select(static g => (g.Key, g.Value))];
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

    private static void Emit(SourceProductionContext context, ImmutableArray<ValueObject> all)
    {
        foreach (var value in all.Distinct().OrderBy(static v => v.FullyQualifiedName, System.StringComparer.Ordinal))
        {
            if (value.Problem is { } problem)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask129, value.Location?.ToLocation(), value.Name, value.TextKind ?? value.ValueBase, "'" + problem + "'"));
                continue;
            }

            if (value.Generic || value.Parameters.Count == 0)
            {
                continue;
            }

            context.AddSource(
                value.FullyQualifiedName.Replace("global::", "").Replace('<', '_').Replace('>', '_') + ".Value.g.cs",
                SourceText.From(Render(value), Encoding.UTF8));
        }
    }

    /// <summary>One primary-constructor parameter, as the generated members take it.</summary>
    private sealed record Parameter(
        string Name, string Type, string OptionalType, bool IsText, bool IsValueType, bool Nullable, bool Formattable, string? Iso = null);

    /// <summary>The column facets a value object declares, as constants; null where it declares none.</summary>
    private sealed record ColumnFacets(int? MaxLength, int? Precision, int? Scale);

    private sealed record ValueObject(
        string FullyQualifiedName,
        string Name,
        string Namespace,
        string ValueBase,
        string? TextKind,
        EquatableArray<string> Containers,
        bool Generic,
        EquatableArray<Parameter> Parameters,
        ColumnFacets Facets,
        ShapeFacets Shape,
        bool HasFieldSeam,
        bool OwnToString,
        bool Ordered,
        string? Problem,
        SymbolLocation? Location)
    {
        /// <summary>
        ///     Holds one value — Text, Number, Integer, Day or Moment over one parameter, or Value over one member — which
        ///     forms ask directly.
        /// </summary>
        public bool OneValue => ValueBase is "Text" or "Number" or "Integer" or "Day" or "Moment" or "Value" && Parameters.Count == 1;
    }
}
