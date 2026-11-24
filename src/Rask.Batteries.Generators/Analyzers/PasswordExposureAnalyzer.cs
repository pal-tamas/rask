using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK173 — an aggregate or entity stores a value object declared from <c>Rask.Values.Password</c>; RASK174 — a
///     query's or a command's result, a subscription's item or an event carries one outward.
/// </summary>
/// <remarks>
///     A password is input only: it comes in from a form, is checked and hashed, and goes nowhere. Both are errors,
///     found however deep the password sits — in a value object the entity holds, a list of them, a nested record of
///     the result. A command that takes one (<c>record SignIn(SignInPassword Password) : ICommand&lt;Token&gt;</c>) is
///     what it is for, and reported by neither.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PasswordExposureAnalyzer : DiagnosticAnalyzer
{
    private const string Cqrs = "Rask.Cqrs";
    private const int MaxDepth = 6;

    private static readonly DiagnosticDescriptor Rask173 = new(
        "RASK173",
        "An aggregate stores a password",
        "'{0}.{1}' stores the password '{2}' — store its hash, and keep the password on the form or the command that brings it in",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A value object declared from Rask.Values.Password holds a password as it was typed. An aggregate "
                     + "or entity that holds one — directly, in a value object, or in a list — would write it to a column "
                     + "and show it on the generated read face. Hash it where it is checked and store the hash.",
        helpLinkUri: DiagnosticHelp.Link("RASK173"));

    private static readonly DiagnosticDescriptor Rask174 = new(
        "RASK174",
        "A password is sent back",
        "'{0}' sends the password '{1}' back through '{2}' — a password comes in, and is never part of a result, an item or an event",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A value object declared from Rask.Values.Password is input only: a form or a command brings it to "
                     + "the server. A query's or a command's result, a subscription's item and an event are written "
                     + "outward — to the browser, a log, another service — so none of them may hold one.",
        helpLinkUri: DiagnosticHelp.Link("RASK174"));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask173, Rask174);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMessage, SymbolKind.NamedType);
    }

    // RASK173: a stored member of an entity that reaches a password.
    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property is not { IsStatic: false, IsIndexer: false } ||
            !AggregateShape.IsEntity(property.ContainingType) ||
            !IsStored(property) ||
            PasswordIn(property.Type) is not { } password)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask173, property.Locations.FirstOrDefault(), property.ContainingType.Name, property.Name, password.Name));
    }

    // RASK174: what a message sends outward — a result, an item, an event itself — that reaches a password.
    private static void AnalyzeMessage(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        foreach (var (outward, through) in Outward(type))
        {
            if (PasswordIn(outward) is { } password)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask174, type.Locations.FirstOrDefault(), type.Name, password.Name, through));
                return;
            }
        }
    }

    // The types a message writes outward, with the interface that says so.
    private static IEnumerable<(ITypeSymbol Outward, string Through)> Outward(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            yield break;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.ContainingNamespace?.ToDisplayString() is not Cqrs)
            {
                continue;
            }

            switch (@interface.MetadataName)
            {
                case "IQuery`1" or "ICommand`1" or "ISubscription`1":
                    yield return (@interface.TypeArguments[0], @interface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    break;
                case "IEvent":
                    yield return (type, "IEvent");
                    break;
            }
        }
    }

    // The password value object a type is, or holds however deep: in a nullable, an array, a type argument
    // (List<T>, a dictionary's values) or a public property of a type of the app's own.
    private static INamedTypeSymbol? PasswordIn(ITypeSymbol type)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<(ITypeSymbol Type, int Depth)>();
        pending.Push((type, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > MaxDepth || !seen.Add(current))
            {
                continue;
            }

            if (AggregateShape.IsPassword(current))
            {
                return (INamedTypeSymbol)current;
            }

            foreach (var held in Held(current))
            {
                pending.Push((held, depth + 1));
            }
        }

        return null;
    }

    private static IEnumerable<ITypeSymbol> Held(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            yield return array.ElementType;
            yield break;
        }

        if (type is not INamedTypeSymbol named)
        {
            yield break;
        }

        foreach (var argument in named.TypeArguments)
        {
            yield return argument;
        }

        // A type of the app's own is walked through its members; the framework's and the BCL's are not.
        if (named.DeclaringSyntaxReferences.Length == 0)
        {
            yield break;
        }

        foreach (var property in named.GetMembers().OfType<IPropertySymbol>()
                     .Where(static p => p is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public }))
        {
            yield return property.Type;
        }
    }

    // A setter of any visibility, or the compiler's field behind a get-only auto-property: a computed one is not state.
    private static bool IsStored(IPropertySymbol property) =>
        property.SetMethod is not null ||
        property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
}
