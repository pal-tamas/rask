using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK187 — EF Core's <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> over an aggregate's rows:
///     <c>await db.Pairings.Where(p =&gt; p.Pump == id).ExecuteDeleteAsync()</c> is <c>await Pairing.DeleteWhere(p =&gt; p.Pump == id)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The raw statement skips what the aggregate's own set write keeps: <c>UpdatedAt</c> and <c>Version</c>, its
///         declared locks, the one-aggregate rule, the data-changed notices a live page repaints on.
///     </para>
///     <para>
///         The quick-fix is offered only where the rewrite is the same statement: the awaitable form, on a <c>DbSet</c>
///         straight, or behind one <c>Where(x =&gt; …)</c>. The predicate, the setters and the token ride as additional
///         locations, in that order (<see cref="WhereProperty" /> and <see cref="TokenProperty" /> say which are there).
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSetWriteAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The write to call in its place, <c>UpdateWhere</c> or <c>DeleteWhere</c>; set only when the fix applies.</summary>
    public const string WriteProperty = "Write";

    /// <summary>The aggregate's name as the call site would write it.</summary>
    public const string AggregateProperty = "Aggregate";

    /// <summary>Set ("1") when a predicate is the first additional location.</summary>
    public const string WhereProperty = "Where";

    /// <summary>Set ("1") when a cancellation-token argument is the last additional location.</summary>
    public const string TokenProperty = "Token";

    private static readonly DiagnosticDescriptor Rask187 = new(
        "RASK187",
        "A raw ExecuteUpdate or ExecuteDelete writes an aggregate's rows",
        "'{0}' writes {1} rows past what its set write keeps — UpdatedAt, Version, its locks, the live notices — call '{1}.{2}(…)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every aggregate has UpdateWhere and DeleteWhere: one statement over the rows a predicate matches, "
                     + "inside a Db.Transaction, that stamps UpdatedAt and Version, asks its LockedBy, counts toward the "
                     + "one-aggregate rule and tells the pages showing the rows. EF Core's ExecuteUpdate/ExecuteDelete "
                     + "does none of it. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK187"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask187);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (WriteOf(call.TargetMethod) is not { } write ||
            call.TargetMethod.TypeArguments.Length != 1 ||
            call.TargetMethod.TypeArguments[0] is not INamedTypeSymbol aggregate ||
            !AggregateShape.IsAggregate(aggregate))
        {
            return;
        }

        var named = aggregate.ToMinimalDisplayString(call.SemanticModel!, call.Syntax.SpanStart);
        var properties = ImmutableDictionary<string, string?>.Empty.Add(AggregateProperty, named);
        var additional = new List<Location>();
        if (call.TargetMethod.Name.EndsWith("Async", StringComparison.Ordinal) && Rows(call) is var (source, where) && IsSet(source))
        {
            properties = properties.Add(WriteProperty, write);
            if (where is not null)
            {
                additional.Add(where.Syntax.GetLocation());
                properties = properties.Add(WhereProperty, "1");
            }

            if (Argument(call, "setPropertyCalls") is { } setters)
            {
                additional.Add(setters.Syntax.GetLocation());
            }

            if (Argument(call, "cancellationToken") is { } token)
            {
                additional.Add(token.Syntax.GetLocation());
                properties = properties.Add(TokenProperty, "1");
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask187, call.Syntax.GetLocation(), additional, properties, call.TargetMethod.Name, aggregate.Name, write));
    }

    // ExecuteUpdate(Async) → UpdateWhere, ExecuteDelete(Async) → DeleteWhere; EF Core's own, nothing else of the name.
    private static string? WriteOf(IMethodSymbol method)
    {
        if (!IsEfCore(method.ContainingNamespace))
        {
            return null;
        }

        return method.Name switch
        {
            "ExecuteUpdate" or "ExecuteUpdateAsync" => "UpdateWhere",
            "ExecuteDelete" or "ExecuteDeleteAsync" => "DeleteWhere",
            _ => null,
        };
    }

    // The rows the statement writes: a set straight, or `set.Where(x => …)` — the predicate then handed back too.
    private static (IOperation Source, IOperation? Where)? Rows(IInvocationOperation call)
    {
        if (call.Arguments.FirstOrDefault()?.Value is not { } receiver)
        {
            return null;
        }

        receiver = Unwrapped(receiver);
        if (receiver is IInvocationOperation { TargetMethod.Name: "Where", Arguments.Length: 2 } where &&
            string.Equals(where.TargetMethod.ContainingType.ToDisplayString(), "System.Linq.Queryable", StringComparison.Ordinal) &&
            Unwrapped(where.Arguments[1].Value) is IDelegateCreationOperation or IAnonymousFunctionOperation &&
            where.TargetMethod.Parameters[1].Type is INamedTypeSymbol { TypeArguments.Length: 1 } expression &&
            expression.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 2 })
        {
            return (Unwrapped(where.Arguments[0].Value), where.Arguments[1].Value);
        }

        return (receiver, null);
    }

    private static bool IsSet(IOperation source) =>
        source.Type is INamedTypeSymbol { Name: "DbSet", Arity: 1 } set && IsEfCore(set.ContainingNamespace);

    private static bool IsEfCore(INamespaceSymbol? space) =>
        string.Equals(space?.ToDisplayString(), "Microsoft.EntityFrameworkCore", StringComparison.Ordinal);

    private static IOperation Unwrapped(IOperation operation) =>
        operation is IConversionOperation { IsImplicit: true, Operand: var operand } ? Unwrapped(operand) : operation;

    // An argument the caller wrote (not a default filled in).
    private static IOperation? Argument(IInvocationOperation call, string parameter) =>
        call.Arguments.FirstOrDefault(a =>
            a.ArgumentKind == ArgumentKind.Explicit && string.Equals(a.Parameter?.Name, parameter, StringComparison.Ordinal))?.Value;
}
