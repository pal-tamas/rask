using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK142 — <c>Task.WhenAll</c> over database work inside the work of a <c>Db.Transaction(…)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Everything a transaction's work reads and writes runs on its one connection, which takes one command at a
///         time: two reads started together make EF Core refuse the second ("a second operation was started on this
///         context instance"). Await them one after the other.
///     </para>
///     <para>
///         Database work is a call to an entity's own statics (the generated <c>Find</c>, <c>Where</c>, <c>Count</c>,
///         <c>Update</c> …), to Rask.Data, or to EF Core — written in the <c>WhenAll</c>'s arguments, or in the
///         initializer of a local they name. A <c>WhenAll</c> over anything else (HTTP calls, say) is not reported.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConcurrentTransactionWorkAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask142 = new(
        "RASK142",
        "Database work run side by side inside a transaction",
        "'{0}' starts '{1}' together inside Db.Transaction, whose work shares one connection that runs one command at a time — await them one after the other",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The work of a Db.Transaction runs every read and write on the transaction's one connection. "
                     + "Task.WhenAll over two of them starts a second command while the first is running, which EF "
                     + "Core refuses at run time. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK142"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask142);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Data.Db") is not { } db)
            {
                return;
            }

            start.RegisterOperationAction(context => Analyze(context, db), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol db)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: "WhenAll", IsStatic: true } method ||
            !IsTask(method.ContainingType) ||
            !InsideTransaction(call, db))
        {
            return;
        }

        var work = call.Arguments.SelectMany(argument => DatabaseCalls(argument.Value, context.CancellationToken)).FirstOrDefault();
        if (work is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask142, call.Syntax.GetLocation(), "Task.WhenAll", OwnerOf(work).Name + "." + work.Name));
        }
    }

    // Whether a lambda around the call, however deep, is the work handed to Db.Transaction.
    private static bool InsideTransaction(IOperation operation, INamedTypeSymbol db)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation &&
                current.Parent is IDelegateCreationOperation { Parent: IArgumentOperation { Parent: IInvocationOperation outer } } &&
                outer.TargetMethod is { Name: "Transaction" } transaction &&
                SymbolEqualityComparer.Default.Equals(transaction.ContainingType, db))
            {
                return true;
            }
        }

        return false;
    }

    // The database calls an argument makes — a call, or a method handed on (`ids.Select(Tank.Find)`) — in the
    // expression itself, or in the initializer of a local it names.
    private static IEnumerable<IMethodSymbol> DatabaseCalls(IOperation argument, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var operation in argument.DescendantsAndSelf())
        {
            if (Called(operation) is { } target)
            {
                yield return target;
            }
            else if (operation is ILocalReferenceOperation { Local: var local } && InitializerOf(local, argument.SemanticModel, cancellationToken) is { } initializer)
            {
                foreach (var inner in initializer.DescendantsAndSelf().Select(Called).OfType<IMethodSymbol>())
                {
                    yield return inner;
                }
            }
        }
    }

    // The type a member is called on: the entity an extension block extends, else the declaring type.
    private static ITypeSymbol OwnerOf(IMethodSymbol method) =>
        method.ContainingType is { IsExtension: true, ExtensionParameter.Type: var extended } ? extended : method.ContainingType;

    private static IMethodSymbol? Called(IOperation operation) =>
        operation switch
        {
            IInvocationOperation { TargetMethod: var target } when IsDatabase(target) => target,
            IMethodReferenceOperation { Method: var target } when IsDatabase(target) => target,
            _ => null,
        };

    private static IOperation? InitializerOf(ILocalSymbol local, SemanticModel? model, System.Threading.CancellationToken cancellationToken)
    {
        if (model is null)
        {
            return null;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == model.SyntaxTree &&
                model.GetOperation(reference.GetSyntax(cancellationToken), cancellationToken) is IVariableDeclaratorOperation { Initializer.Value: { } value })
            {
                return value;
            }
        }

        return null;
    }

    // The generated reads and writes (the members of an extension block over an entity — `Order.Find(id)`,
    // `order.Save()` — or an entity's own statics), Rask.Data's, or EF Core's.
    private static bool IsDatabase(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if ((method.IsStatic || type.IsExtension) && AggregateShape.IsEntity(OwnerOf(method)))
        {
            return true;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return Within(ns, "Rask.Data") || Within(ns, "Microsoft.EntityFrameworkCore");
    }

    private static bool Within(string ns, string root) =>
        string.Equals(ns, root, System.StringComparison.Ordinal) || ns.StartsWith(root + ".", System.StringComparison.Ordinal);

    private static bool IsTask(INamedTypeSymbol type) =>
        type is { Name: "Task", Arity: 0, ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };
}
