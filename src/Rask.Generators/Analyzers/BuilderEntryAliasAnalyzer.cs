using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK037 — a <c>using</c> alias that a builder entry hides.
///     <para>
///         A component's members beat a <c>using</c> alias in simple-name lookup, so
///         <c>using B = Some.Namespace;</c> in a file that declares a component makes <c>B.Thing</c>
///         inside that component resolve to the <c>&lt;b&gt;</c> entry instead of the alias. The
///         compiler then reports <b>CS1061</b> ("'B' does not contain a definition for 'Thing'") — a
///         hard error naming a type nobody wrote, pointing at the use rather than at the alias, and
///         out of reach of a code fix because by then the alias has already lost. This says it at the
///         alias, where the rename goes.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BuilderEntryAliasAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask037 = new(
        "RASK037",
        "'using' alias is hidden by a builder entry",
        "The 'using' alias '{0}' is hidden by the builder entry '{0}' on '{1}' — inside a component, a member beats an alias in simple-name lookup, so '{0}.Something' resolves to the entry and fails with CS1061. Rename the alias.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every component type contributes an entry property named after itself, so any single "
                     + "name a tag or component uses is taken inside a component body. A 'using' alias with "
                     + "the same name still resolves everywhere else in the file, which is what makes the "
                     + "resulting CS1061 so confusing — the alias appears to work until the first use inside "
                     + "a component. Rename the alias (the two-letter tag names are the ones to watch: 'B', "
                     + "'I', 'P', 'A', 'Td').",
        helpLinkUri: DiagnosticHelp.Link("RASK037"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask037);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentFullName);
            if (component is null)
            {
                return;
            }

            // One pass per file rather than per using directive: the aliases and the components that
            // would hide them are both file-scoped facts, and the overwhelmingly common case (no alias
            // at all) then costs a single filtered walk of the file's top level.
            start.RegisterSemanticModelAction(ctx => Analyze(ctx, component));
        });
    }

    private static void Analyze(SemanticModelAnalysisContext context, INamedTypeSymbol component)
    {
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);

        List<UsingDirectiveSyntax>? aliases = null;
        foreach (var node in root.DescendantNodes(static n =>
                     n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
        {
            if (node is UsingDirectiveSyntax { Alias: not null } directive)
            {
                (aliases ??= new List<UsingDirectiveSyntax>()).Add(directive);
            }
        }

        if (aliases is null)
        {
            return;
        }

        // The types whose members are in scope for the aliased names: every component declared in this
        // file, plus Component itself for a `global using` alias, which is in scope for components in
        // files this one cannot see.
        var scopes = new List<INamedTypeSymbol>();
        foreach (var declaration in root.DescendantNodes())
        {
            if (declaration is not ClassDeclarationSyntax classDeclaration)
            {
                continue;
            }

            if (context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is { } symbol
                && BuilderEntry.DerivesFromComponent(symbol, component))
            {
                scopes.Add(symbol);
            }
        }

        foreach (var directive in aliases)
        {
            var name = directive.Alias!.Name.Identifier.ValueText;
            var isGlobal = !directive.GlobalKeyword.IsKind(SyntaxKind.None);

            var hidden = Hidden(scopes, name, component)
                         ?? (isGlobal ? BuilderEntry.FindEntry(component, name, component) : null);
            if (hidden is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask037,
                directive.Alias.Name.GetLocation(),
                name,
                hidden.ContainingType.ToDisplayString()));
        }
    }

    private static ISymbol? Hidden(List<INamedTypeSymbol> scopes, string name, INamedTypeSymbol component)
    {
        foreach (var scope in scopes)
        {
            if (BuilderEntry.FindEntry(scope, name, component) is { } entry)
            {
                return entry;
            }
        }

        return null;
    }
}
