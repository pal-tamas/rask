using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK189 — <c>b.Job&lt;E&gt;()</c> in an aggregate's <c>Configure</c>, where the aggregate declares no
///     <c>static Task On(E e)</c>: the declaration places a job nothing would ever enqueue.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventJobWithoutHandlerAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask189 = new(
        "RASK189",
        "A job is declared for an event the aggregate does not handle",
        "'{0}' declares Job<{1}>(), but has no 'static Task On({1} e)' for the job to run",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "b.Job<E>() runs the aggregate's own On(E) as a job instead of in the outbox's drain. Without that "
                     + "handler the event reaches nothing here, so the declaration says something that never happens: "
                     + "declare 'static Task On(E e)' on the aggregate, or remove the line.",
        helpLinkUri: DiagnosticHelp.Link("RASK189"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask189);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: "Job", IsGenericMethod: true, TypeArguments.Length: 1 } job ||
            !string.Equals(job.ContainingType?.ToDisplayString(), "Rask.Background.JobsModelBuilderExtensions", StringComparison.Ordinal) ||
            call.Arguments.FirstOrDefault()?.Value is not { } receiver ||
            Unwrapped(receiver).Type is not INamedTypeSymbol { Name: "EntityTypeBuilder", TypeArguments.Length: 1 } builder ||
            builder.TypeArguments[0] is not INamedTypeSymbol aggregate)
        {
            return;
        }

        var eventType = job.TypeArguments[0];
        if (!Handles(aggregate, eventType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask189, call.Syntax.GetLocation(), aggregate.Name, eventType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static IOperation Unwrapped(IOperation value) =>
        value is IConversionOperation { IsImplicit: true, Operand: var operand } ? operand : value;

    private static bool Handles(INamedTypeSymbol aggregate, ITypeSymbol eventType) =>
        aggregate.GetMembers("On").OfType<IMethodSymbol>().Any(m =>
            m.IsStatic && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, eventType));
}
