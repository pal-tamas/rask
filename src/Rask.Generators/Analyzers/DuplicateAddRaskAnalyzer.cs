using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK060 — warn when the same service collection is handed to AddRask twice. The second call builds a
// fresh RaskCultureOptions, runs the caller's configureCulture over it, and then registers it with
// TryAddSingleton — which keeps the FIRST registration. So the languages named in the second call are
// discarded while the call itself compiles and reads correctly, and the app ships with no supported
// cultures at all. Worse, AddRaskCulture still flips the process-wide RaskCulture.IsEnabled, so the
// negotiation path turns on over an empty catalog.
//
// Deliberately scoped to two calls in the SAME method body ON THE SAME RECEIVER, rather than
// compilation-wide: a test file legitimately calls AddRask once per test over its own ServiceCollection,
// and flagging that would make the rule noise. Suppressible.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateAddRaskAnalyzer : DiagnosticAnalyzer
{
    private const string AddRaskMethod = "AddRask";

    // Both hosts define AddRask, and both funnel culture options through the same TryAddSingleton.
    private static readonly ImmutableHashSet<string> HostAssemblies =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Rask.Server", "Rask.Wasm.Hosting");

    private static readonly DiagnosticDescriptor Rask056 = new(
        "RASK060",
        "AddRask is called twice on the same service collection",
        "'{0}' is passed to AddRask more than once — the later call's options are silently discarded; "
        + "merge them into a single AddRask call",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "A second AddRask on the same service collection does not add to the first. Its options are "
        + "registered with TryAddSingleton, which keeps the registration already there, so everything the "
        + "later call configures is dropped — most visibly configureCulture, leaving an app that named its "
        + "languages shipping with none. Nothing fails, because the call compiles and the services resolve. "
        + "Pass every option to one AddRask call instead.",
        helpLinkUri: DiagnosticHelp.Link("RASK060"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask056);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // A per-tree pass, like RASK024: the rule compares calls against each other, which
        // RegisterSyntaxNodeAction cannot do statelessly.
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var model = context.SemanticModel;
        var root = model.SyntaxTree.GetRoot(context.CancellationToken);

        // Keyed on the enclosing body and the receiver as written, so two collections configured side by
        // side in one method stay separate and only a genuine double-registration groups together.
        var groups = new Dictionary<(SyntaxNode Scope, string Receiver), List<InvocationExpressionSyntax>>();

        foreach (var node in root.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax inv || MethodName(inv) is not AddRaskMethod)
            {
                continue; // Cheap syntactic filter before the semantic lookup.
            }

            if (model.GetSymbolInfo(inv, context.CancellationToken).Symbol is not IMethodSymbol method
                || !string.Equals(method.Name, AddRaskMethod, StringComparison.Ordinal)
                || method.ContainingAssembly?.Name is not { } assembly
                || !HostAssemblies.Contains(assembly))
            {
                continue;
            }

            if (Receiver(inv) is not { } receiver)
            {
                continue;
            }

            var scope = EnclosingScope(inv);
            var key = (scope, receiver);
            if (!groups.TryGetValue(key, out var calls))
            {
                groups[key] = calls = [];
            }

            calls.Add(inv);
        }

        foreach (var calls in groups.Values.Where(static c => c.Count > 1))
        {
            // Report every call after the first: those are the ones whose options go nowhere, and the ones
            // whose arguments have to move into the surviving call.
            foreach (var extra in calls.OrderBy(static c => c.SpanStart).Skip(1))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask056, NameLocation(extra), Receiver(extra)));
            }
        }
    }

    // The body a call sits in: a method, a local function, an accessor, or — for top-level statements,
    // which is what Program.cs is — the compilation unit itself.
    private static SyntaxNode EnclosingScope(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax or CompilationUnitSyntax)
            {
                return current;
            }
        }

        return node.SyntaxTree.GetRoot();
    }

    // The receiver as the author wrote it ("builder.Services", "services"). Text rather than a symbol
    // because the interesting case is a local or a property chain, and comparing the spelling is what
    // keeps two DIFFERENT collections in one method from being treated as one.
    private static string? Receiver(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Expression.ToString(),
        _ => null
    };

    private static string? MethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => null
    };

    private static Location NameLocation(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => inv.Expression.GetLocation()
    };
}
