using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK186 — <c>X.Update(id, x =&gt; x.M(…))</c> where <c>M</c> is an operation with a generated entry:
///     <c>await Supply.Update(id, s =&gt; s.Change(details), version: v)</c> is <c>await Supply.Change(id, v, details)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The entry loads, runs and saves in one transaction just as the update does, and also asks the roles the
///         aggregate's <c>Configure</c> names for the operation — the update asks none — so the call a form makes and
///         the call code makes are the same one.
///     </para>
///     <para>
///         Reported only when the rewrite means the same: the lambda does nothing but call the operation on the
///         aggregate it is handed, with arguments that do not read it; no <c>db:</c> is given; a <c>Versioned</c>
///         aggregate is given an <c>int</c> version (the entry takes no <c>null</c>); and the updated aggregate the
///         call hands back is not used. The quick-fix takes the id, the operation call, the version and the token from
///         the additional locations, in that order (<see cref="VersionProperty" /> and <see cref="TokenProperty" /> say
///         which of the last two are there).
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UpdateThroughOperationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set ("1") when a version argument is among the additional locations.</summary>
    public const string VersionProperty = "Version";

    /// <summary>Set ("1") when a cancellation-token argument is the last additional location.</summary>
    public const string TokenProperty = "Token";

    /// <summary>The operation's name, which the fix calls in place of <c>Update</c>.</summary>
    public const string OperationProperty = "Operation";

    private static readonly DiagnosticDescriptor Rask186 = new(
        "RASK186",
        "An update runs an operation that has a generated entry",
        "'{0}.Update(id, … => ….{1}(…))' runs '{1}' without the roles its generated entry asks — call '{0}.{1}(id, …)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every public method of a partial aggregate gets a static entry of the same name that loads the "
                     + "aggregate, asks the roles its Configure names, runs the method and saves, in one transaction. "
                     + "An Update whose lambda only calls that method does the same without the roles. "
                     + "A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK186"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask186);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: "Update", IsStatic: true } update ||
            OwnerOf(update) is not { } aggregate ||
            !AggregateShape.IsAggregate(aggregate) ||
            !ResultUnused(call) ||
            Argument(call, "db") is not null ||
            Argument(call, "apply") is not { } apply ||
            Argument(call, "id") is not { } id ||
            OperationCall(apply) is not { } operation ||
            Entry(aggregate, operation.TargetMethod) is null)
        {
            return;
        }

        var version = Argument(call, "version");
        if (AggregateShape.HasVersion(aggregate) != version is not null || (version is not null && !IsInt(version)))
        {
            return;
        }

        var token = Argument(call, "cancellationToken");
        var additional = new List<Location> { id.Syntax.GetLocation(), operation.Syntax.GetLocation() };
        var properties = ImmutableDictionary<string, string?>.Empty.Add(OperationProperty, operation.TargetMethod.Name);
        if (version is not null)
        {
            additional.Add(version.Syntax.GetLocation());
            properties = properties.Add(VersionProperty, "1");
        }

        if (token is not null)
        {
            additional.Add(token.Syntax.GetLocation());
            properties = properties.Add(TokenProperty, "1");
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask186, call.Syntax.GetLocation(), additional, properties, aggregate.Name, operation.TargetMethod.Name));
    }

    // The type a member is called on: the aggregate an extension block extends, else the declaring type.
    private static INamedTypeSymbol? OwnerOf(IMethodSymbol method) =>
        method.ContainingType is { IsExtension: true, ExtensionParameter.Type: INamedTypeSymbol extended } ? extended : method.ContainingType;

    // An argument the caller wrote (not a default filled in), unwrapped from its implicit conversion.
    private static IOperation? Argument(IInvocationOperation call, string parameter)
    {
        var argument = call.Arguments.FirstOrDefault(a =>
            a.ArgumentKind == ArgumentKind.Explicit && string.Equals(a.Parameter?.Name, parameter, StringComparison.Ordinal));
        return argument?.Value is IConversionOperation { IsImplicit: true, Operand: var operand } ? operand : argument?.Value;
    }

    private static bool IsInt(IOperation version) => version.Type?.SpecialType == SpecialType.System_Int32;

    // `await X.Update(…);` as a statement, `X.Update(…);`, or the task handed on as a plain Task: what the entry
    // returns. A caller that reads the updated aggregate keeps its update.
    private static bool ResultUnused(IInvocationOperation call) =>
        call.Parent switch
        {
            IExpressionStatementOperation => true,
            IAwaitOperation { Parent: IExpressionStatementOperation } => true,
            IConversionOperation { Type: INamedTypeSymbol { Name: "Task", Arity: 0 } } => true,
            _ => false,
        };

    // `x => x.M(a, b)`, `async x => await x.M(a, b)`: the one call the lambda makes, on the aggregate it is handed,
    // with every argument written in order and none reading the aggregate.
    private static IInvocationOperation? OperationCall(IOperation apply)
    {
        if (apply is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Symbol.Parameters.Length: 1 } lambda })
        {
            return null;
        }

        var handed = lambda.Symbol.Parameters[0];

        var statements = lambda.Body.Operations
            .Where(static statement => statement is not IReturnOperation { ReturnedValue: null, IsImplicit: true })
            .ToList();
        if (statements.Count != 1)
        {
            return null;
        }

        var made = Unwrapped(statements[0] switch
        {
            IExpressionStatementOperation { Operation: var expression } => expression,
            IReturnOperation { ReturnedValue: var value } => value,
            _ => null,
        });

        return made is IInvocationOperation { Instance: IParameterReferenceOperation { Parameter: var on } } invocation &&
               SymbolEqualityComparer.Default.Equals(on, handed) &&
               invocation.Arguments.Length == invocation.TargetMethod.Parameters.Length &&
               invocation.Arguments.All(static argument => argument is { ArgumentKind: ArgumentKind.Explicit, Syntax: ArgumentSyntax { NameColon: null } }) &&
               !invocation.Arguments.Any(argument => Reads(argument.Value, handed))
            ? invocation
            : null;
    }

    // Through `await` and an implicit conversion: `await x.M(…)` is the call.
    private static IOperation? Unwrapped(IOperation? operation)
    {
        if (operation is IAwaitOperation { Operation: var awaited })
        {
            operation = awaited;
        }

        return operation is IConversionOperation { IsImplicit: true, Operand: var operand } ? operand : operation;
    }

    private static bool Reads(IOperation value, IParameterSymbol handed) =>
        value.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>()
            .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Parameter, handed));

    // The generated entry of `operation`: a static of the same name on the aggregate, written by the operation
    // generator, taking the id, the version when there is one, the operation's own parameters and a token.
    private static IMethodSymbol? Entry(INamedTypeSymbol aggregate, IMethodSymbol operation)
    {
        var lead = AggregateShape.HasVersion(aggregate) ? 2 : 1;
        return aggregate.GetMembers(operation.Name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(entry =>
                entry.IsStatic &&
                OperationEntryCallAnalyzer.IsEntry(entry) &&
                entry.Parameters.Length == lead + operation.Parameters.Length + 1 &&
                operation.Parameters
                    .Select((parameter, i) => SymbolEqualityComparer.Default.Equals(parameter.Type, entry.Parameters[lead + i].Type))
                    .All(static same => same));
    }
}
