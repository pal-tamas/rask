using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK043 — a component is being built from code that is not a component, with the factory not
///     imported.
///     <para>
///         The builder surface is reachable only from <i>inside</i> a component: its entries are
///         <b>inherited members</b>, which is the whole point (a static-imported property loses to a
///         same-named type — CS0119 — while a member of the enclosing type wins). Everything else — a
///         test class, a static markup helper, a fixture — reaches components through the generated
///         <c>Generated.Foo(…)</c> factory, which is a <i>method</i> and so beats the same-named type
///         under C#'s invocable-member rule. That is why the factory works in these positions and an
///         entry cannot.
///     </para>
///     <para>
///         When the <c>using static …Generated;</c> is missing, the simple name binds to the component
///         <b>type</b> instead, and the compiler says <b>CS0119</b> ("'Div' is a type, which is not valid
///         in the given context") — sometimes followed by CS0021 on the <c>[…]</c> that would have
///         carried the children. Nothing in that names Rask, the factory, or the one line that fixes it.
///         This does.
///     </para>
///     <para>
///         RASK118 is the same mistake one dot later: <c>OrderReceipt.OrderId(…)</c>, written in a job or a
///         mail sender. The head binds to the component <b>type</b>, so the chain step reads as an instance
///         member asked of a type and the compiler says <b>CS0120</b> ("an object reference is required"),
///         which reads like a <c>static</c> problem and names neither Rask nor the attribute that fixes it.
///         An element head (<c>Div.Class(…)</c>) binds to nothing at all there, which is RASK043's CS0103.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FactoryNotImportedAnalyzer : DiagnosticAnalyzer
{
    private const string FactoryClassName = "Generated";

    private static readonly DiagnosticDescriptor Rask043 = new(
        "RASK043",
        "A component name is used in a type that has no chain entries",
        "'{0}' is not a chain entry here, so this does not compile (CS0119 when it names a type, CS0103 when nothing does): entries are members of a component or a markup host, and '{1}' is neither — derive '{1}' from 'Rask.Core.RaskMarkup', or mark it '[Rask.Core.RaskMarkup]' when its base is taken or it is a 'static class'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Chain entries are reachable only from inside a type that HAS them: entries are inherited "
                     + "members, because a static-imported property loses to a same-named type in scope (CS0119) "
                     + "while a member of the enclosing type wins. A component is one such type; so is anything "
                     + "deriving from 'Rask.Core.RaskMarkup', which is Component's own base and carries the "
                     + "framework entries and nothing else — that is the answer for a test class, a fixture or a "
                     + "helper that builds demo components. A type that cannot spend its base slot — one whose "
                     + "base belongs to someone else, or a 'static class' — says the same thing with the "
                     + "'[RaskMarkup]' attribute instead. Anywhere else the name binds to the type, and the "
                     + "compiler reports CS0119, CS0120 or CS0021 — none of which says what is missing.",
        helpLinkUri: DiagnosticHelp.Link("RASK043"));

    private static readonly DiagnosticDescriptor Rask118 = new(
        "RASK118",
        "A chain step is called on a component type outside a markup host",
        "'{0}.{1}' asks the component type '{0}' for its step '{1}', so this does not compile (CS0120): '{0}' is a chain entry only inside a component or a markup host, and '{2}' is neither — mark '{2}' '[Rask.Core.RaskMarkup]' and make it 'partial', or derive it from 'Rask.Core.RaskMarkup' when its base is free",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A component is built from its chain entry — the member named after it — and entries are "
                     + "members of a component or a markup host only. In any other type (a job, a mail sender, a "
                     + "domain-event handler) the name binds to the component TYPE, so its first step reads as an "
                     + "instance member asked of a type and the compiler reports CS0120. '[RaskMarkup]' on the "
                     + "enclosing 'partial' class makes it a host and puts the entries in scope.",
        helpLinkUri: DiagnosticHelp.Link("RASK118"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask043, Rask118);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(ComponentSymbols.ComponentFullName);
            if (component is null)
            {
                return;
            }

            // Both shapes a factory call takes: `Div(Class: "x")` and the children indexer that follows
            // it, `Div[…]`. Each binds the same failing simple name, so either one on its own is enough
            // to report — and reporting from both is what covers a call with no arguments.
            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component, ((InvocationExpressionSyntax)ctx.Node).Expression),
                SyntaxKind.InvocationExpression);
            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component, ((ElementAccessExpressionSyntax)ctx.Node).Expression),
                SyntaxKind.ElementAccessExpression);

            // The chain's own shape, `OrderReceipt.OrderId(…)` / `Div.Class(…)`: the head is the entry, and
            // the step after the dot is what the compiler trips on.
            start.RegisterSyntaxNodeAction(ctx => AnalyzeStep(ctx, component), SyntaxKind.SimpleMemberAccessExpression);
        });
    }

    private static void AnalyzeStep(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (access.Expression is not SimpleNameSyntax head || IsInNameof(access))
        {
            return;
        }

        var enclosing = context.ContainingSymbol?.ContainingType;
        if (enclosing is null || BuilderEntry.IsEntryHost(enclosing, component))
        {
            return;
        }

        if (Bound(context, head) is { } type)
        {
            // A static member of a component (`Card.Url()`, a constant) is reached through its type on
            // purpose; only an instance member — a step — is the one asked of the wrong thing.
            if (BuilderEntry.DerivesFromComponent(type, component) && NamesInstanceMember(context, access))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask118, access.GetLocation(), type.Name, access.Name.Identifier.ValueText,
                    enclosing.ToDisplayString()));
            }

            return;
        }

        if (FrameworkEntry(context, head) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask043, head.GetLocation(), head.Identifier.ValueText, enclosing.ToDisplayString()));
        }
    }

    // `nameof(OrderReceipt.OrderId)` names an instance member through its type, and that is legal C#.
    private static bool IsInNameof(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(static call =>
            call.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });

    private static bool NamesInstanceMember(SyntaxNodeAnalysisContext context, MemberAccessExpressionSyntax access)
    {
        var info = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken);
        return info.Symbol is { } symbol
            ? IsInstanceMember(symbol)
            : info.CandidateSymbols.Any(IsInstanceMember);
    }

    private static bool IsInstanceMember(ISymbol symbol) =>
        symbol is { IsStatic: false, Kind: SymbolKind.Property or SymbolKind.Method or SymbolKind.Field or SymbolKind.Event };

    private static void Analyze(
        SyntaxNodeAnalysisContext context, INamedTypeSymbol component, ExpressionSyntax target)
    {
        // A qualified call (`Generated.Div(…)`, `Parts.Loading()`) already says where it comes from; only
        // a bare simple name can be the one that lost to its own type.
        if (target is not SimpleNameSyntax name)
        {
            return;
        }

        if ((Bound(context, name) ?? FrameworkEntry(context, name)) is not { } type
            || !BuilderEntry.DerivesFromComponent(type, component)
            || type.ContainingNamespace is not { IsGlobalNamespace: false } ns)
        {
            return;
        }

        // Inside a component — or any other RaskMarkup host — this cannot happen: the entry is a member
        // of the enclosing type and wins the lookup outright, and if it somehow did, the answer there is
        // the chain, not an import.
        var enclosing = context.ContainingSymbol?.ContainingType;
        if (enclosing is null || BuilderEntry.IsEntryHost(enclosing, component))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask043,
            name.GetLocation(),
            type.Name,
            enclosing.ToDisplayString(),
            ns.ToDisplayString() + "." + FactoryClassName));
    }

    // A name that resolved to nothing but is one of the framework's entries: an element's type is named
    // after its DOM interface (HTMLDivElement), so outside a host `Div` no longer loses to a type — it simply
    // does not exist (CS0103), and says just as little about why.
    private static INamedTypeSymbol? FrameworkEntry(SyntaxNodeAnalysisContext context, SimpleNameSyntax name)
    {
        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        if (info.Symbol is not null || info.CandidateSymbols.Length != 0
            || context.SemanticModel.Compilation.GetTypeByMetadataName("Rask.Markup") is not { } markup)
        {
            return null;
        }

        foreach (var member in markup.GetMembers(name.Identifier.ValueText))
        {
            if (member is IPropertySymbol { IsStatic: true, Type: INamedTypeSymbol produced })
            {
                return produced;
            }
        }

        return null;
    }

    // The component type the name resolved to, or null. The expression does not compile, so the symbol
    // may arrive as the bound one (Roslyn binds `Div(…)` to the type and reports CS0119) or as a
    // candidate — take either, and only when it really is a named type rather than a method group.
    private static INamedTypeSymbol? Bound(SyntaxNodeAnalysisContext context, SimpleNameSyntax name)
    {
        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        if (info.Symbol is INamedTypeSymbol bound)
        {
            return bound;
        }

        foreach (var candidate in info.CandidateSymbols)
        {
            if (candidate is INamedTypeSymbol named)
            {
                return named;
            }
        }

        return null;
    }
}
