using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Finds the application's user type — the one class deriving from <c>Rask.Auth.Authenticatable</c>, or from
/// <c>Authenticatable&lt;TKey&gt;</c> for a typed id over a <c>Guid</c> or a text key — and emits a
/// <c>[ModuleInitializer]</c> naming it to <c>Rask.Auth.AuthUser</c>,
/// so <c>AddRaskAuth()</c> and <c>modelBuilder.AddRaskAuth()</c> need no type argument.
/// </summary>
/// <remarks>
/// Rask ships no user class of its own, so without this every call would have to name the app's. The
/// binding closes over the type at compile time — generated, never reflected — which is what keeps the
/// account tables through a trimmed publish.
/// </remarks>
[Generator]
public sealed class AuthUserGenerator : IIncrementalGenerator
{
    private const string AuthenticatableBase = "Authenticatable";
    private const string AuthNamespace = "Rask.Auth";

    private static readonly DiagnosticDescriptor Rask074 = new(
        "RASK074",
        "More than one user type",
        "'{0}' and '{1}' both derive from Authenticatable, so Rask cannot tell which one is this app's "
        + "user; auth is left unwired. Keep one user type — the other is an aggregate like any other, "
        + "and does not need to derive from Authenticatable to be mapped.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The user type is found rather than named, which needs there to be one. With two, "
                     + "picking either would map one users table and silently strand the other, "
                     + "and the app would look wired until the first sign-in.",
        helpLinkUri: DiagnosticHelp.Link("RASK074"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!.Value);

        context.RegisterSourceOutput(candidates.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax declaration ||
            ctx.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Abstract or generic cannot be closed over; a user type also needs the parameterless
        // constructor registration builds a new user with.
        if (symbol.IsAbstract || symbol.IsStatic || symbol.IsGenericType || KeyTypeOf(symbol) is not { } keyType)
        {
            return null;
        }

        if (!symbol.InstanceConstructors.Any(static c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
        {
            return null;
        }

        var key = keyType.Length == 1 ? keyType[0] : null;
        return new Candidate(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            key?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            IsTypedOverGuid(key),
            SymbolLocation.From(symbol));
    }

    // A typed id over a Guid — `UserId(Guid Value)`, declared here or in a referenced assembly — which the binding
    // crosses with two lambdas, so Rask.Auth carries the Guid it holds without reflecting over it.
    private static bool IsTypedOverGuid(ITypeSymbol? key) =>
        key is not null && (TypedIdShape.Of(key) == TypedIdValue.Guid || TypedIdShape.OfReferenced(key) == TypedIdValue.Guid);

    // The type arguments the user type's Authenticatable is closed with: none for the Guid-keyed Authenticatable, whose
    // binding names no key, and Authenticatable<TKey>'s TKey otherwise. Null for a class that derives from neither.
    private static ImmutableArray<ITypeSymbol>? KeyTypeOf(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name is AuthenticatableBase &&
                current.ContainingNamespace?.ToDisplayString() is AuthNamespace)
            {
                return current.TypeArguments;
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var users = candidates
            .Distinct()
            .OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        if (users.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask074,
                users[1].Location?.ToLocation(),
                users[0].FullyQualifiedName,
                users[1].FullyQualifiedName));
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Rask.Auth.Generated;");
        source.AppendLine();
        source.AppendLine("/// <summary>Names this assembly's user type to Rask.Auth.</summary>");
        source.AppendLine("internal static class __RaskAuthUser");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.Append("    internal static void Initialize() => global::Rask.Auth.AuthUser.Use<")
            .Append(users[0].FullyQualifiedName);
        if (users[0].Key.Length > 0)
        {
            source.Append(", ").Append(users[0].Key);
        }

        if (users[0].IsTyped)
        {
            source.Append(">(static value => new ").Append(users[0].Key).AppendLine("(value), static id => id.Value);");
        }
        else
        {
            source.AppendLine(">();");
        }
        source.AppendLine("}");

        context.AddSource("__RaskAuthUser.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private readonly record struct Candidate(string FullyQualifiedName, string Key, bool IsTyped, SymbolLocation? Location);
}
