using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Generators;

/// <summary>
///     Completes the one line a typed id is — <c>public readonly partial record struct VehicleId(int Value);</c> — into
///     a value that parses, prints, travels as JSON and binds from a route.
/// </summary>
/// <remarks>
///     What counts as a typed id is <see cref="TypedIdShape" />'s rule, so the routes generator, which cannot see this
///     output, agrees with it. Its EF Core value converter is the model registry's (Rask.Data), which reads the same
///     positional <c>Value</c>.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed partial class TypedIdGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var ids = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => TypedIdShape.IsCandidate(node),
                static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol((RecordDeclarationSyntax)ctx.Node, ct) is INamedTypeSymbol symbol
                    ? Describe(symbol)
                    : null)
            .Where(static id => id is not null)
            .Select(static (id, _) => id!)
            .Collect();

        context.RegisterSourceOutput(ids, static (spc, all) =>
        {
            // A typed id split over two partial files is matched once per file; it is completed once.
            foreach (var id in all.GroupBy(static id => id.FullName, StringComparer.Ordinal).Select(static g => g.First()))
            {
                spc.AddSource(id.HintName, SourceText.From(Render(id), Encoding.UTF8));
            }
        });
    }

    private static TypedId? Describe(INamedTypeSymbol symbol)
    {
        var value = TypedIdShape.Of(symbol);
        if (value == TypedIdValue.None)
        {
            return null;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString();

        return new TypedId(
            ns,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            value,
            DeclaresCreate(symbol),
            Declares(symbol, "Parse"),
            Declares(symbol, "TryParse"),
            Declares(symbol, nameof(ToString)),
            Declares(symbol, "CompareTo") || Declares(symbol, "op_LessThan"),
            symbol.GetAttributes().Any(static a =>
                a.AttributeClass?.ToDisplayString() is "System.Text.Json.Serialization.JsonConverterAttribute"));
    }

    // What the author already wrote wins: the generated half only fills in what is missing.
    private static bool Declares(INamedTypeSymbol symbol, string name) =>
        symbol.GetMembers(name).Any(static member => !member.IsImplicitlyDeclared);

    // Only a parameterless static Create() stands in for the generated one: a hand-written Create(Guid) or Create(string)
    // is an overload beside it, not a clash.
    private static bool DeclaresCreate(INamedTypeSymbol symbol) =>
        symbol.GetMembers("Create").OfType<IMethodSymbol>()
            .Any(static method => !method.IsImplicitlyDeclared && method.IsStatic && method.Parameters.Length == 0);

    /// <summary>One typed id, as far as its generated half needs to know it.</summary>
    private sealed record TypedId(
        string? Namespace,
        string Name,
        string FullName,
        TypedIdValue Value,
        bool HasCreate,
        bool HasParse,
        bool HasTryParse,
        bool HasToString,
        bool HasCompareTo,
        bool HasJsonConverter)
    {
        public string HintName => FullName.Replace("global::", "") + ".TypedId.g.cs";
    }
}
