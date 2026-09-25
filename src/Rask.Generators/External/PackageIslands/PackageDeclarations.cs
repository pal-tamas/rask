using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.External.PackageIslands;

/// <summary>One component a package declaration exports — <c>Button</c> of <c>Mui</c> — as an island.</summary>
/// <param name="Name">The island's type name: the declaration's name and <paramref name="Member" />, <c>MuiButton</c>.</param>
/// <param name="Member">Its entry on the declaration: <c>Mui.Button</c>.</param>
/// <param name="Export">The export it imports, as written in <c>Exports</c>.</param>
internal sealed record PackageExportIsland(string Name, string Member, string Export);

/// <summary>What a package declaration's <c>Module</c> and <c>Exports</c> say, read out of its syntax.</summary>
/// <param name="Runtime">The runtime its base class names.</param>
/// <param name="Module">The package, or null when it could not be read.</param>
/// <param name="Islands">One island per export, in the order written.</param>
/// <param name="Failed">The member that is not a constant (<c>Module</c> or <c>Exports</c>), for RASK059, or null.</param>
/// <param name="FailedAt">Where that member is.</param>
internal sealed record PackageDeclaration(
    string Runtime,
    string? Module,
    IReadOnlyList<PackageExportIsland> Islands,
    string? Failed,
    Location? FailedAt);

/// <summary>
///     Package declarations — <c>sealed partial class Mui : ReactPackage</c> — expanded into the islands they export.
/// </summary>
/// <remarks>
///     <para>
///         Shared for the reason <see cref="PackageIslandProps" /> is: <c>ExternalGenerator</c> declares each island
///         class and writes its props, the factory generator gives it a chain, and neither can see the other's output.
///         So both expand the declaration here, from the same syntax, and cannot disagree about a name.
///     </para>
///     <para>
///         Read out of the SYNTAX, like a single island's <c>Module</c>: the build needs the same values before the
///         compile, and only a literal survives that trip.
///     </para>
/// </remarks>
internal static class PackageDeclarations
{
    /// <summary>The runtime a package declaration's base class names, or null when it is not one.</summary>
    public static string? RuntimeOf(INamedTypeSymbol type)
    {
        if (type.IsAbstract)
        {
            return null;
        }

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            var name = t.OriginalDefinition.ToDisplayString();
            foreach (var (baseName, runtime, _) in ExternalRuntimes.All)
            {
                if (string.Equals(name, PackageBaseOf(baseName), StringComparison.Ordinal))
                {
                    return runtime;
                }
            }
        }

        return null;
    }

    /// <summary><c>Rask.External.ReactComponent</c>'s package class, <c>Rask.External.ReactPackage</c>.</summary>
    public static string PackageBaseOf(string componentBase) =>
        componentBase.Substring(0, componentBase.Length - "Component".Length) + "Package";

    /// <summary>The declaration's module and islands, or null when <paramref name="type" /> is not a declaration.</summary>
    public static PackageDeclaration? Read(INamedTypeSymbol type)
    {
        if (RuntimeOf(type) is not { } runtime)
        {
            return null;
        }

        var module = ModuleLiteral.Read(type);
        if (module.Failed)
        {
            return new PackageDeclaration(runtime, null, [], "Module", module.Location);
        }

        var exports = ReadExports(type, out var failedAt);
        if (exports is null)
        {
            return new PackageDeclaration(runtime, module.Value, [], "Exports", failedAt);
        }

        var islands = new List<PackageExportIsland>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports)
        {
            var member = MemberName(export);
            if (member.Length != 0 && seen.Add(member))
            {
                islands.Add(new PackageExportIsland(type.Name + member, member, export));
            }
        }

        return new PackageDeclaration(runtime, module.Value, islands, null, null);
    }

    /// <summary>
    ///     An export's member name: <c>Button</c> stays <c>Button</c>, a dotted <c>Switch.Root</c> is <c>SwitchRoot</c>,
    ///     and a Lit tag <c>sl-switch</c> is <c>SlSwitch</c>.
    /// </summary>
    public static string MemberName(string export)
    {
        var sb = new StringBuilder(export.Length);
        var upper = true;
        foreach (var c in export)
        {
            if (c is '.' or '-' or '_')
            {
                upper = true;
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                return string.Empty;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.Length != 0 && char.IsLetter(sb[0]) ? sb.ToString() : string.Empty;
    }

    /// <summary>
    ///     The facts about one exported island, as <see cref="PackageIslandProps.Facts" /> gives them for a declared
    ///     class — with the declaration standing in for the class the island never has in source.
    /// </summary>
    /// <remarks>
    ///     Its snapshot sits beside the declaration, and the names a generated prop may not take are the runtime base's
    ///     members and the island's own name: there is no hand-written half to collide with.
    /// </remarks>
    public static IslandFacts Facts(
        INamedTypeSymbol declaration, PackageDeclaration read, PackageExportIsland island, INamedTypeSymbol? runtimeBase)
    {
        var directories = declaration.DeclaringSyntaxReferences
            .Select(static r => AssetPairing.NormalizeDirectory(r.SyntaxTree.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static d => d, StringComparer.Ordinal)
            .ToArray();

        var reserved = new SortedSet<string>(StringComparer.Ordinal) { island.Name };
        for (var t = runtimeBase; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == "Rask.Core.RaskMarkup")
            {
                continue;
            }

            foreach (var member in t.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Private)
                {
                    reserved.Add(member.Name);
                }
            }
        }

        var typeNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var existing in declaration.ContainingNamespace.GetTypeMembers())
        {
            typeNames.Add(existing.Name);
        }

        foreach (var sibling in read.Islands)
        {
            typeNames.Add(sibling.Name);
        }

        return new IslandFacts(
            island.Name,
            declaration.ContainingNamespace.IsGlobalNamespace ? null : declaration.ContainingNamespace.ToDisplayString(),
            read.Runtime,
            read.Module,
            island.Export,
            PackageIslandProps.IsExternallyVisible(declaration),
            new EquatableArray<string>(directories),
            default,
            new EquatableArray<string>(reserved.ToArray()),
            new EquatableArray<string>(typeNames.ToArray()));
    }

    /// <summary>The runtime's island base class, <c>Rask.External.ReactComponent</c> for <c>react</c>.</summary>
    public static INamedTypeSymbol? RuntimeBase(Compilation compilation, string runtime)
    {
        foreach (var (baseName, key, _) in ExternalRuntimes.All)
        {
            if (string.Equals(key, runtime, StringComparison.Ordinal))
            {
                return compilation.GetTypeByMetadataName(baseName);
            }
        }

        return null;
    }

    /// <summary>
    ///     The literals an <c>Exports</c> override returns — <c>=&gt; ["Button", "Card"];</c>, or the same array written
    ///     <c>new[] { … }</c> — or null when it returns anything else.
    /// </summary>
    private static IReadOnlyList<string>? ReadExports(INamedTypeSymbol type, out Location? location)
    {
        var property = type.GetMembers("Exports").OfType<IPropertySymbol>().FirstOrDefault();
        location = property?.Locations.FirstOrDefault(static l => l.IsInSource);
        var syntax = property?.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();

        var expression = syntax?.ExpressionBody?.Expression
                         ?? syntax?.Initializer?.Value
                         ?? syntax?.AccessorList?.Accessors
                             .FirstOrDefault(static a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.ExpressionBody?.Expression;

        IEnumerable<ExpressionSyntax>? elements = expression switch
        {
            CollectionExpressionSyntax collection => collection.Elements.Select(static e =>
                e is ExpressionElementSyntax element ? element.Expression : null!),
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions,
            ArrayCreationExpressionSyntax array when array.Initializer is not null => array.Initializer.Expressions,
            _ => null,
        };

        if (elements is null)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var element in elements)
        {
            if (element is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return null;
            }

            values.Add(literal.Token.ValueText);
        }

        return values;
    }
}
