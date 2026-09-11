using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.External;

/// <summary>What an island's <c>Module</c> override says, read out of its syntax.</summary>
/// <param name="Value">The literal, when one could be read.</param>
/// <param name="Declared">Whether the class overrides <c>Module</c> at all.</param>
/// <param name="Failed">Whether it overrides it with something that is not a constant string (RASK059).</param>
/// <param name="Location">Where the override is, for diagnostics.</param>
internal readonly record struct ModuleOverride(string? Value, bool Declared, bool Failed, Location? Location);

/// <summary>Reads the constant an island's <c>Module</c> override returns.</summary>
/// <remarks>
///     Read out of the SYNTAX rather than evaluated, because the value is needed at build time — the
///     bundler generates one entry module per island long before any of this code could run. So only a
///     literal will do. Shared so that the island generator, which reports RASK059, and the factory
///     generator, which pairs a package island with its props snapshot, read the same four forms the same
///     way.
/// </remarks>
internal static class ModuleLiteral
{
    public static ModuleOverride Read(INamedTypeSymbol type)
    {
        var property = type.GetMembers("Module").OfType<IPropertySymbol>().FirstOrDefault();
        if (property is null)
        {
            return default;
        }

        var location = property.Locations.FirstOrDefault(static l => l.IsInSource);

        var syntax = property.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();

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

    /// <summary>The text of a string literal expression, or null for anything else.</summary>
    private static string? Literal(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
}
