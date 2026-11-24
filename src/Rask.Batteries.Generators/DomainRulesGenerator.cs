using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Tells <c>Rask.Data.GeneratedDomainRules</c> what every aggregate and entity in the compilation is asked before a
/// save writes it: the validation attributes on its properties and its type, whether it validates itself
/// (<c>IValidatableObject</c>), and its own <c>Validate()</c> and <c>ValidateDelete()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Validate()</c> and <c>ValidateDelete()</c> are found by convention: an instance method with no parameters
/// returning <c>IEnumerable&lt;Problem&gt;</c> or <c>IAsyncEnumerable&lt;Problem&gt;</c>, of any accessibility. A
/// private one is reached through an <c>[UnsafeAccessor]</c>, so the entity needs no <c>partial</c> and nothing is
/// found by reflection.
/// </para>
/// <para>
/// The attributes are built exactly as the generated form model's write builds them
/// (<see cref="ValidationAttributeSource" />), so a save and a <c>Create(model)</c> ask the same thing.
/// </para>
/// </remarks>
[Generator]
public sealed class DomainRulesGenerator : IIncrementalGenerator
{
    private const string ProblemType = "Rask.Values.Problem";
    private const string Enumerable = "System.Collections.Generic.IEnumerable<T>";
    private const string AsyncEnumerable = "System.Collections.Generic.IAsyncEnumerable<T>";
    private const string ProblemFqn = "global::Rask.Values.Problem";
    private const string WordsType = "Rask.Core.Forms.DataAnnotationsFieldValidator";

    private static readonly string[] SelfValidating =
    [
        "System.ComponentModel.DataAnnotations.IValidatableObject",
        "System.ComponentModel.DataAnnotations.IAsyncValidatableObject",
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) => GetRuled(ctx))
            .Where(static entity => entity is not null)
            .Select(static (entity, _) => entity!);

        // Rask.Core's catalog words a built-in attribute's failure in the reader's language, as the generated
        // Create(model) does; an assembly without it keeps .NET's text.
        var catalog = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(WordsType)?.GetMembers("Message").Length > 0);

        context.RegisterSourceOutput(
            entities.Collect().Combine(catalog), static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static Ruled? GetRuled(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax declaration ||
            ctx.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol ||
            !AggregateShape.IsMappedEntity(symbol) ||
            !AggregateShape.IsNameableFromGeneratedCode(symbol))
        {
            return null;
        }

        var properties = CheckedProperties(symbol).ToList();
        var typeChecks = AppTypes(symbol)
            .SelectMany(static t => t.GetAttributes())
            .Where(ValidationAttributeSource.IsValidation)
            .Select(ValidationAttributeSource.Construction)
            .ToList();
        var validate = Convention(symbol, "Validate");
        var validateDelete = Convention(symbol, "ValidateDelete");
        var selfValidating = symbol.AllInterfaces.Any(static i => SelfValidating.Contains(i.ToDisplayString(), StringComparer.Ordinal));

        if (properties.Count == 0 && typeChecks.Count == 0 && validate is null && validateDelete is null && !selfValidating)
        {
            return null;
        }

        return new Ruled(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<RuledProperty>(properties),
            new EquatableArray<string>(typeChecks),
            validate,
            validateDelete);
    }

    // The entity and the app's own bases it derives from — never Rask.Data's, whose members are the framework's.
    private static IEnumerable<INamedTypeSymbol> AppTypes(INamedTypeSymbol symbol)
    {
        for (var current = symbol;
             current is not null && current.SpecialType != SpecialType.System_Object &&
             !string.Equals(current.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);
             current = current.BaseType)
        {
            yield return current;
        }
    }

    // Every property, the derived declaration first, that carries an attribute the server can run.
    private static IEnumerable<RuledProperty> CheckedProperties(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in AppTypes(symbol).SelectMany(static t => t.GetMembers()).OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || !seen.Add(property.Name))
            {
                continue;
            }

            var checks = property.GetAttributes().Where(ValidationAttributeSource.IsValidation).ToList();
            if (checks.Count == 0)
            {
                continue;
            }

            yield return new RuledProperty(
                property.Name,
                ValidationAttributeSource.NameOf(property),
                HeldValue(property.Type),
                new EquatableArray<string>(checks.Select(ValidationAttributeSource.Construction)));
        }
    }

    // A value object of one value is checked by that value, as its form model carries it: [MaxLength(5)] on a Code asks
    // the text inside, not the record.
    private static string? HeldValue(ITypeSymbol type)
    {
        if (AggregateShape.ValueObjectTypeOf(type) is not { } valueObject || !AggregateShape.HoldsOneValue(valueObject))
        {
            return null;
        }

        var held = AggregateShape.StoredProperties(valueObject).First();
        return "static held => held is " + valueObject.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               " value ? (object?)value." + held.Name + " : null";
    }

    // `Validate()` / `ValidateDelete()`: an instance method with no parameters that yields problems, on the entity or a
    // base of the app's. One on a generic base cannot be named by an accessor and is not one.
    private static ConventionRule? Convention(INamedTypeSymbol symbol, string name)
    {
        foreach (var type in AppTypes(symbol))
        {
            foreach (var method in type.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 } &&
                    !type.IsGenericType &&
                    YieldsProblems(method.ReturnType) is { } awaited)
                {
                    return new ConventionRule(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), awaited);
                }
            }
        }

        return null;
    }

    // Whether the method's sequence is awaited; null when it yields something other than problems.
    private static bool? YieldsProblems(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } sequence ||
            !string.Equals(sequence.TypeArguments[0].ToDisplayString(), ProblemType, StringComparison.Ordinal))
        {
            return null;
        }

        return sequence.OriginalDefinition.ToDisplayString() switch
        {
            Enumerable => false,
            AsyncEnumerable => true,
            _ => null,
        };
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Ruled> candidates, bool fromCatalog)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Distinct because a partial entity contributes one candidate per declaration with a base list.
        var ruled = candidates.Distinct().OrderBy(static r => r.FullyQualifiedName, StringComparer.Ordinal).ToList();
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();
        s.AppendLine("namespace Rask.Data.Generated;");
        s.AppendLine();
        s.AppendLine("/// <summary>What this assembly's aggregates and entities are asked before a save writes them.</summary>");
        s.AppendLine("file static class __RaskDomainRules");
        s.AppendLine("{");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Initialize()");
        s.AppendLine("    {");

        var accessors = new StringBuilder();
        for (var i = 0; i < ruled.Count; i++)
        {
            AppendAdd(s, accessors, ruled[i], i.ToString(CultureInfo.InvariantCulture), fromCatalog);
        }

        s.AppendLine("    }");
        s.Append(accessors);
        s.AppendLine("}");

        context.AddSource("__RaskDomainRules.g.cs", SourceText.From(s.ToString(), Encoding.UTF8));
    }

    private static void AppendAdd(StringBuilder s, StringBuilder accessors, Ruled entity, string index, bool fromCatalog)
    {
        s.Append("        global::Rask.Data.GeneratedDomainRules.Add<").Append(entity.FullyQualifiedName).AppendLine(">(");
        s.AppendLine("            [");
        foreach (var property in entity.Properties)
        {
            s.Append("                new global::Rask.Data.CheckedProperty(\"").Append(property.Name).Append("\", static () => ")
                .Append(property.Named).Append(", ").Append(property.Unwrap ?? "null");
            foreach (var check in property.Checks)
            {
                s.Append(", ").Append(check);
            }

            s.AppendLine("),");
        }

        s.AppendLine("            ],");
        s.Append("            [").Append(string.Join(", ", entity.TypeChecks)).AppendLine("],");
        s.Append("            ").Append(Rule(accessors, entity, entity.Validation, "Validate", index)).AppendLine(",");
        s.Append("            ").Append(Rule(accessors, entity, entity.DeleteValidation, "ValidateDelete", index)).AppendLine(",");
        s.Append("            ").Append(fromCatalog ? "global::" + WordsType + ".Message" : "null").AppendLine(");");
    }

    // The delegate the registry keeps, and the accessor it calls: a synchronous rule is handed over as a sequence that
    // completes as it is read.
    private static string Rule(StringBuilder accessors, Ruled entity, ConventionRule? rule, string name, string index)
    {
        if (rule is null)
        {
            return "null";
        }

        var accessor = name + "_" + index;
        var sequence = rule.Awaited
            ? "global::System.Collections.Generic.IAsyncEnumerable<" + ProblemFqn + ">"
            : "global::System.Collections.Generic.IEnumerable<" + ProblemFqn + ">";

        accessors.AppendLine();
        accessors.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"")
            .Append(name).AppendLine("\")]");
        accessors.Append("    private static extern ").Append(sequence).Append(' ').Append(accessor).Append('(')
            .Append(rule.DeclaringType).AppendLine(" entity);");

        return rule.Awaited
            ? "static (" + entity.FullyQualifiedName + " entity) => " + accessor + "(entity)"
            : "static (" + entity.FullyQualifiedName + " entity) => global::System.Linq.AsyncEnumerable.ToAsyncEnumerable(" + accessor + "(entity))";
    }

    private sealed record Ruled(
        string FullyQualifiedName,
        EquatableArray<RuledProperty> Properties,
        EquatableArray<string> TypeChecks,
        ConventionRule? Validation,
        ConventionRule? DeleteValidation);

    private sealed record RuledProperty(string Name, string Named, string? Unwrap, EquatableArray<string> Checks);

    private sealed record ConventionRule(string DeclaringType, bool Awaited);
}
