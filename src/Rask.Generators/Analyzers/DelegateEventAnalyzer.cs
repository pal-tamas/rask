using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK096 — an event declared as a delegate.
//
// The chain receives on the component, so `Save.OnSave(fn)` has to fall through member lookup to the
// generated extension setter. A delegate-typed property is INVOCABLE, so lookup stops at it and the call
// binds as an invocation of the property (CS1593) — the setter is unreachable. `Callback<T>` is a struct
// with no Invoke-by-call, which is what keeps it reachable, and it takes both handler shapes anyway.
//
// Only the EVENT shapes: `Action…` and a `Func…` returning a bare `Task`/`ValueTask`. A delegate that
// returns anything else is a template (`Func<T, Component>`) or a selector (`Func<T, bool>`), not an event.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DelegateEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask096 = new(
        "RASK096",
        "Event declared as a delegate",
        "Declare '{0}' as {1}, not {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "A delegate-typed property is invocable, so 'x.OnSave(fn)' binds as a call of the property and its "
        + "chain setter is unreachable. Declare the event as a non-nullable Callback, Callback<T> or "
        + "Callback<T1, T2> and fire it with 'await OnSave.Invoke(x)'.",
        DiagnosticHelp.Link("RASK096"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask096);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentMetadataName) is not { } component)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, component), SymbolKind.Property);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol component)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.ContainingType is not { TypeKind: TypeKind.Class } owner
            || !BuilderEntry.DerivesFromComponent(owner, component)
            || !BuilderEntry.IsSettableByAChain(property)
            || EventArguments(property.Type) is not { } arguments)
        {
            return;
        }

        var callback = arguments.Length == 0
            ? "Callback"
            : "Callback<" + string.Join(", ", arguments.Select(static a => a.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">";
        var written = property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax declaration)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask096, declaration.Type.GetLocation(), property.Name, callback, written));
            }
        }
    }

    /// <summary>
    ///     The arguments an event-shaped delegate passes — <c>Action&lt;T&gt;</c> and <c>Func&lt;T, Task&gt;</c>
    ///     both pass <c>T</c> — or <c>null</c> when <paramref name="type" /> is not one a Callback can stand
    ///     in for (any other return type, or more than two arguments).
    /// </summary>
    internal static ImmutableArray<ITypeSymbol>? EventArguments(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } named
            || named.ContainingNamespace?.ToDisplayString() != "System")
        {
            return null;
        }

        var arguments = named.TypeArguments;
        if (named.Name == "Action")
        {
            return arguments.Length <= 2 ? arguments : null;
        }

        if (named.Name != "Func" || arguments.Length == 0 || !IsBareTask(arguments[arguments.Length - 1]))
        {
            return null;
        }

        return arguments.Length <= 3 ? arguments.RemoveAt(arguments.Length - 1) : null;
    }

    private static bool IsBareTask(ITypeSymbol type) =>
        type is INamedTypeSymbol { Arity: 0, Name: "Task" or "ValueTask" } task
        && task.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
}
