using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.External;

/// <summary>What an island's <c>Module</c> (or <c>Export</c>) override says, read out of its syntax.</summary>
/// <param name="Value">The literal, when one could be read.</param>
/// <param name="Declared">Whether the class overrides the property at all.</param>
/// <param name="Failed">Whether it overrides it with something that is not a constant string (RASK059).</param>
/// <param name="Location">Where the override is, for diagnostics.</param>
internal readonly record struct ModuleOverride(string? Value, bool Declared, bool Failed, Location? Location);

/// <summary>Reads the constant an island's <c>Module</c> or <c>Export</c> override returns.</summary>
/// <remarks>
///     Read out of the SYNTAX rather than evaluated, because the value is needed at build time — the
///     bundler generates one entry module per island long before any of this code could run. So only a
///     literal will do. Shared so that the island generator, which reports RASK059, and the factory
///     generator, which pairs a package island with its props snapshot, read the same four forms the same
///     way.
/// </remarks>
internal static class ModuleLiteral
{
    public static ModuleOverride Read(INamedTypeSymbol type) => Read(type, "Module", exportOverride: false);

    /// <summary>The package export an island's <c>Export</c> override names, read the same way.</summary>
    /// <remarks>
    ///     Only the OVERRIDE of <c>ExternalComponent.Export</c> counts. An island may well have a prop of its own named
    ///     <c>Export</c> — a chart's "export as PNG" flag — and reading that as the package export would report RASK059
    ///     against a prop the author never meant as one and generate nothing. <c>=&gt; null</c> is the base's own
    ///     default spelled out, so it reads as "no export", not as an expression the build cannot evaluate.
    /// </remarks>
    public static ModuleOverride ReadExport(INamedTypeSymbol type) => Read(type, "Export", exportOverride: true);

    private static ModuleOverride Read(INamedTypeSymbol type, string name, bool exportOverride)
    {
        var property = type.GetMembers(name).OfType<IPropertySymbol>()
            .FirstOrDefault(p => !exportOverride || (p.IsOverride && !p.IsStatic));
        if (property is null)
        {
            return default;
        }

        var location = property.Locations.FirstOrDefault(static l => l.IsInSource);

        var syntax = property.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();

        if (exportOverride && IsNull(syntax?.ExpressionBody?.Expression ?? syntax?.Initializer?.Value))
        {
            return new ModuleOverride(null, true, false, location);
        }

        if (Literal(syntax?.ExpressionBody?.Expression) is { } arrow)
        {
            return new ModuleOverride(arrow, true, false, location);
        }

        if (Literal(syntax?.Initializer?.Value) is { } initializer)
        {
            return new ModuleOverride(initializer, true, false, location);
        }

        // A getter body — `get => "…";` or `get { return "…"; }` — reads identically to the author, so
        // accepting only the two forms above would be an arbitrary distinction.
        var getter = syntax?.AccessorList?.Accessors
            .FirstOrDefault(static a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

        if (Literal(getter?.ExpressionBody?.Expression) is { } getterArrow)
        {
            return new ModuleOverride(getterArrow, true, false, location);
        }

        var returned = getter?.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
        if (getter?.Body?.Statements.Count == 1 && Literal(returned?.Expression) is { } returnedLiteral)
        {
            return new ModuleOverride(returnedLiteral, true, false, location);
        }

        return new ModuleOverride(null, true, true, location);
    }

    private static bool IsNull(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression);

    /// <summary>The text of a string literal expression, or null for anything else.</summary>
    private static string? Literal(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
}
