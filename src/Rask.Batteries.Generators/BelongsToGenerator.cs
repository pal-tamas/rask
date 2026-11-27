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
///     Completes <c>public partial BelongsTo&lt;Vehicle&gt;? Vehicle { get; }</c>: the stored foreign key beside it —
///     <c>public VehicleId? VehicleId { get; private set; }</c>, of the vehicle's key type, nullable as the reference is —
///     the reference read from it, and the relationship it maps to (no navigation, no cascade).
/// </summary>
/// <remarks>
///     The key keeps the name and the type a hand-written <c>VehicleId</c> had, so the column, its index and its foreign
///     key are the same and the move is no migration. The reference is <c>[NotMapped]</c>: EF Core maps the id, never the
///     other aggregate, which is what keeps it out of this one's loads and saves.
/// </remarks>
[Generator]
public sealed class BelongsToGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Rask194 = new(
        "RASK194",
        "A BelongsTo cannot be generated",
        "'{0}.{1}' cannot be a BelongsTo: {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "public partial BelongsTo<Vehicle>? Vehicle { get; } on a partial entity is completed with the stored "
                     + "VehicleId, of Vehicle's key type, and maps it as a relationship that never cascades. That needs a "
                     + "partial property and class Rask can add to, an aggregate to refer to that is neither the tenant "
                     + "nor the root holding this row, a free name for the key, and no form model, which does not carry "
                     + "a reference yet.",
        helpLinkUri: DiagnosticHelp.Link("RASK194"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var references = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is PropertyDeclarationSyntax { Type: var type } && Names(type),
                static (ctx, token) => Describe(ctx, token))
            .Where(static reference => reference is not null)
            .Select(static (reference, _) => reference!);

        context.RegisterSourceOutput(references.Collect(), static (spc, all) => Emit(spc, all));
    }

    // BelongsTo<X>, BelongsTo<X>?, Rask.Data.BelongsTo<X>: the syntax alone, before any symbol is asked for.
    private static bool Names(TypeSyntax type) => type switch
    {
        NullableTypeSyntax nullable => Names(nullable.ElementType),
        QualifiedNameSyntax qualified => Names(qualified.Right),
        AliasQualifiedNameSyntax alias => Names(alias.Name),
        GenericNameSyntax { Identifier.ValueText: "BelongsTo", TypeArgumentList.Arguments.Count: 1 } => true,
        _ => false,
    };

    private static Reference? Describe(GeneratorSyntaxContext ctx, CancellationToken token)
    {
        if (ctx.Node is not PropertyDeclarationSyntax declaration ||
            ctx.SemanticModel.GetDeclaredSymbol(declaration, token) is not { } property ||
            BelongsToShape.TargetOf(property.Type) is not { } target)
        {
            return null;
        }

        var entity = property.ContainingType;
        var problems = BelongsToShape.ProblemsOf(property, declaration);
        var key = BelongsToShape.KeyTypeOf(property);

        return new Reference(
            entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            entity.Name,
            entity.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            new EquatableArray<string>([.. ContainersOf(entity)]),
            property.Name,
            AccessibilityOf(declaration),
            target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            key?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            key is { IsValueType: true },
            BelongsToShape.IsOptional(property),
            new EquatableArray<string>([.. problems]),
            SymbolLocation.From(property));
    }

    // `partial class Outer` for each type the entity is nested in, outermost first, then the entity's own.
    private static Stack<string> ContainersOf(INamedTypeSymbol entity)
    {
        var chain = new Stack<string>();
        for (var type = entity; type is not null; type = type.ContainingType)
        {
            chain.Push((type.IsRecord ? "partial record " : "partial class ") + type.Name);
        }

        return chain;
    }

    // The declaration's own access modifiers, which the implementing half must repeat.
    private static string AccessibilityOf(PropertyDeclarationSyntax declaration) =>
        string.Join(" ", declaration.Modifiers
            .Where(static m => m.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.PrivateKeyword)
            .Select(static m => m.ValueText));

    private static void Emit(SourceProductionContext context, ImmutableArray<Reference> all)
    {
        foreach (var entity in all.Distinct().GroupBy(static r => r.EntityTypeName, System.StringComparer.Ordinal).OrderBy(static g => g.Key, System.StringComparer.Ordinal))
        {
            // In the order they are declared, which is the order the keys take as columns.
            var references = entity.OrderBy(static r => r.Location?.FilePath, System.StringComparer.Ordinal).ThenBy(static r => r.Location?.Start).ToList();
            foreach (var reference in references)
            {
                foreach (var problem in reference.Problems)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask194, reference.Location?.ToLocation(), reference.EntityName, reference.Property, problem));
                }
            }

            var generated = references.Where(static r => r.Problems.Count == 0).ToList();
            if (generated.Count > 0)
            {
                context.AddSource(
                    entity.Key.Replace("global::", "") + ".BelongsTo.g.cs",
                    SourceText.From(Render(generated), Encoding.UTF8));
            }
        }
    }

    private static string Render(List<Reference> references)
    {
        var first = references[0];
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (first.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(first.Namespace).AppendLine(";");
            s.AppendLine();
        }

        var depth = 0;
        foreach (var container in first.Containers)
        {
            s.Append(' ', depth * 4).AppendLine(container);
            s.Append(' ', depth * 4).AppendLine("{");
            depth++;
        }

        var indent = new string(' ', depth * 4);

        foreach (var reference in references)
        {
            AppendReference(s, reference, indent);
        }

        while (depth-- > 0)
        {
            s.Append(' ', depth * 4).AppendLine("}");
        }

        AppendRegistration(s, first, references);
        return s.ToString();
    }

    private static void AppendReference(StringBuilder s, Reference reference, string indent)
    {
        var key = reference.Property + BelongsToShape.KeySuffix;
        var keyType = reference.KeyTypeName + (reference.Optional ? "?" : "");
        var of = "global::Rask.Data.BelongsTo.Of<" + reference.TargetTypeName + ", " + reference.KeyTypeName + ">";
        var referenceType = "global::Rask.Data.BelongsTo<" + reference.TargetTypeName + ">" + (reference.Optional ? "?" : "");

        s.Append(indent).Append("/// <summary>The id of the <see cref=\"").Append(reference.TargetTypeName).Append("\" /> <see cref=\"")
            .Append(reference.Property).AppendLine("\" /> names: the stored foreign key, assigned by this type alone.</summary>");
        s.Append(indent).Append("public ").Append(keyType).Append(' ').Append(key).Append(" { get; private set; }")
            .AppendLine(!reference.Optional && !reference.KeyIsValueType ? " = default!;" : "");
        s.AppendLine();
        s.Append(indent).AppendLine("[global::System.ComponentModel.DataAnnotations.Schema.NotMapped]");
        s.Append(indent).Append(reference.Access).Append(reference.Access.Length > 0 ? " partial " : "partial ")
            .Append(referenceType).Append(' ').Append(reference.Property).AppendLine(" =>");
        s.Append(indent).Append("    ").AppendLine(reference.Optional
            ? key + " is { } id ? " + of + "(id) : null;"
            : of + "(" + key + ");");
        s.AppendLine();
    }

    // The relationship each reference maps to, declared once the assembly loads.
    private static void AppendRegistration(StringBuilder s, Reference first, List<Reference> references)
    {
        s.AppendLine();
        s.Append("file static class __").Append(first.EntityName).AppendLine("BelongsTo");
        s.AppendLine("{");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Register()");
        s.AppendLine("    {");
        foreach (var reference in references)
        {
            s.Append("        global::Rask.Data.ConventionRegistry.DeclareBelongsTo<").Append(reference.EntityTypeName).Append(", ")
                .Append(reference.TargetTypeName).Append(">(\"").Append(reference.Property).Append(BelongsToShape.KeySuffix).AppendLine("\");");
        }

        s.AppendLine("    }");
        s.AppendLine("}");
    }

    private sealed record Reference(
        string EntityTypeName,
        string EntityName,
        string Namespace,
        EquatableArray<string> Containers,
        string Property,
        string Access,
        string TargetTypeName,
        string KeyTypeName,
        bool KeyIsValueType,
        bool Optional,
        EquatableArray<string> Problems,
        SymbolLocation? Location);
}
