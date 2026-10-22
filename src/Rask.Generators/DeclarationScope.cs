using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Generators;

/// <summary>
///     Lets generated code name a type exactly as its declaration does, when only the declaration can.
/// </summary>
/// <remarks>
///     A type another generator makes — a Rask.Data read face — is an error type here: generators never see each
///     other's output, so it prints as the bare name it was written with. That name means something only where it
///     was written. <see cref="Of" /> rebuilds the lookup at that place as nested namespace blocks, outermost first:
///     the file's own using directives, then for each enclosing namespace its directives and its members (as
///     <c>using global::Ns;</c>), each a layer of its own so a nearer one wins exactly as it does there. Every
///     name in a directive is printed from the root, so the blocks mean the same wherever they are written.
/// </remarks>
internal static class DeclarationScope
{
    /// <summary>
    ///     Whether <paramref name="type" /> names a type this compilation cannot see, and no Rask generator's naming
    ///     rule (<see cref="GeneratedModelShape.GeneratedFqn" />) names it from the root.
    /// </summary>
    public static bool NamesUnseen(ITypeSymbol type, Compilation compilation) => type switch
    {
        IErrorTypeSymbol error => GeneratedModelShape.GeneratedFqn(error, compilation) is null,
        IArrayTypeSymbol array => NamesUnseen(array.ElementType, compilation),
        IPointerTypeSymbol pointer => NamesUnseen(pointer.PointedAtType, compilation),
        INamedTypeSymbol named => named.TypeArguments.Any(t => NamesUnseen(t, compilation)),
        _ => false,
    };

    /// <summary>
    ///     Whether every unseen type in <paramref name="type" /> is a bare name, which <see cref="Of" />'s layers
    ///     resolve. A qualified one (<c>Reads.CompanyRead</c>, <c>Reads</c> unseen too) may go through a namespace
    ///     the layers do not import; an unseen <c>CompanyRead?</c> may be a class or a struct, which <c>typeof</c>
    ///     has to know when <paramref name="typeOf" />.
    /// </summary>
    public static bool Nameable(ITypeSymbol type, Compilation compilation, bool typeOf) => type switch
    {
        IErrorTypeSymbol error => GeneratedModelShape.GeneratedFqn(error, compilation) is not null
                                  || (error.ContainingType is null
                                      && error.ContainingNamespace is null or { IsGlobalNamespace: true }
                                      && error.TypeArguments.All(t => Nameable(t, compilation, typeOf))),
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable when typeOf =>
            !NamesUnseen(nullable.TypeArguments[0], compilation) || GeneratedModelShape.GeneratedFqn(nullable.TypeArguments[0], compilation) is not null,
        IArrayTypeSymbol array => Nameable(array.ElementType, compilation, typeOf),
        IPointerTypeSymbol pointer => Nameable(pointer.PointedAtType, compilation, typeOf),
        INamedTypeSymbol named => named.TypeArguments.All(t => Nameable(t, compilation, typeOf)),
        _ => true,
    };

    /// <summary>
    ///     The using layers, outermost first, that resolve a name as it resolves at <paramref name="declaration" />;
    ///     null when a directive there names something this compilation cannot place.
    /// </summary>
    public static EquatableArray<string>? Of(SyntaxNode declaration, Compilation compilation, CancellationToken cancellation)
    {
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var layers = new List<string>();
        foreach (var block in declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(block, cancellation) is not { } ns)
            {
                return null;
            }

            // `namespace A.B { using U; }` is `namespace A { namespace B { using U; } }`: B's members, then U, then A's.
            layers.Add("using " + ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ";");
            if (Directives(block.Externs, block.Usings, model, false, cancellation) is not { } own)
            {
                return null;
            }

            layers.Add(own);
            var parts = block.Name.ToString().Count(c => c == '.');
            for (var at = ns.ContainingNamespace; parts-- > 0 && at is { IsGlobalNamespace: false }; at = at.ContainingNamespace)
            {
                layers.Add("using " + at.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ";");
            }
        }

        var unit = (CompilationUnitSyntax)declaration.SyntaxTree.GetRoot(cancellation);
        if (Directives(unit.Externs, unit.Usings, model, true, cancellation) is not { } file)
        {
            return null;
        }

        layers.Add(file);
        layers.Reverse();
        return new EquatableArray<string>(layers.Where(layer => layer.Length > 0));
    }

    // Each directive with its name printed from the root; a global using is in scope everywhere already.
    private static string? Directives(
        SyntaxList<ExternAliasDirectiveSyntax> externs,
        SyntaxList<UsingDirectiveSyntax> usings,
        SemanticModel model,
        bool fileLevel,
        CancellationToken cancellation)
    {
        var lines = externs.Select(e => e.WithoutTrivia().ToString()).ToList();
        foreach (var directive in usings.Where(u => u.GlobalKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)))
        {
            if (Rooted(directive, model, fileLevel, cancellation) is not { } line)
            {
                return null;
            }

            lines.Add(line);
        }

        return string.Join("\n", lines);
    }

    private static string? Rooted(UsingDirectiveSyntax directive, SemanticModel model, bool fileLevel, CancellationToken cancellation)
    {
        if (directive.NamespaceOrType is not { } target)
        {
            return null;
        }

        var head = "using " + (directive.StaticKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) ? string.Empty : "static ")
                   + (directive.Alias is { } alias ? alias.Name.Identifier.ValueText + " = " : string.Empty);
        if (target is AliasQualifiedNameSyntax)
        {
            return head + target.WithoutTrivia() + ";";
        }

        switch (model.GetSymbolInfo(target, cancellation).Symbol)
        {
            case INamespaceSymbol ns:
                return head + ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ";";
            case ITypeSymbol type when !NamesUnseen(type, model.Compilation):
                return head + GeneratedModelShape.DisplayName(type, SymbolDisplayFormat.FullyQualifiedFormat, model.Compilation) + ";";
        }

        // Not made yet — another generator's namespace or type. At file level a plain name is looked up from the root.
        return fileLevel && target is NameSyntax and not GenericNameSyntax && !target.DescendantNodes().OfType<GenericNameSyntax>().Any()
            ? head + "global::" + target.WithoutTrivia() + ";"
            : null;
    }
}
