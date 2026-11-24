using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK185 — the work of a <c>Db.Transaction(…)</c> uses an aggregate held in a local declared before the block:
///     <c>var supply = await Supply.Find(id); await Db.Transaction(async () =&gt; { supply.Distribute(…); … });</c>.
/// </summary>
/// <remarks>
///     <para>
///         The read was not part of the transaction: the isolation it asks for does not cover it, so a check the work
///         makes on that copy is a check on what was there before the transaction began, and a retry of the work runs
///         again on the same stale copy — on a <c>Versioned</c> aggregate, into the conflict it cannot get past.
///         Loaded inside the block, every attempt reads what it changes.
///     </para>
///     <para>
///         A local the work assigns itself is not reported, nor an aggregate a parameter hands in. The quick-fix moves
///         the declaration into the block when nothing outside the block uses the local
///         (<see cref="MovableProperty" />); the diagnostic's additional locations are the declaration and the work.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OutsideTransactionLoadAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set ("1") when the declaration can be moved into the block as it is written.</summary>
    public const string MovableProperty = "Movable";

    /// <summary>Set ("1") when the work returns a value, so an expression body becomes a <c>return</c>.</summary>
    public const string ReturnsProperty = "Returns";

    private static readonly DiagnosticDescriptor Rask185 = new(
        "RASK185",
        "An aggregate is loaded outside the transaction that changes it",
        "'{0}' is a '{1}' read before Db.Transaction, so the transaction does not cover the read and a retry runs again on the same copy — load it inside the block",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The work of a Db.Transaction changes an aggregate held in a local declared before the block. "
                     + "That read is outside the transaction: its isolation does not cover it, and a retry of the work "
                     + "runs again on the copy read before the transaction began. Load the aggregate inside the block. "
                     + "A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK185"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask185);

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
        if (!IsTransaction(call.TargetMethod, db) ||
            WorkOf(call) is not { } work ||
            Enclosing(call, db) is not null)
        {
            return;
        }

        // The outermost block only: a nested one joins it, and a local declared in the outer work is inside.
        foreach (var captured in work.Descendants().OfType<ILocalReferenceOperation>()
                     .Where(reference => IsAggregate(reference.Local.Type) && DeclaredOutside(reference.Local, work.Syntax))
                     .GroupBy(reference => reference.Local, SymbolEqualityComparer.Default))
        {
            var references = captured.ToList();
            if (references.Exists(IsWritten))
            {
                continue;
            }

            var local = references[0].Local;
            var declaration = DeclarationOf(local, context.CancellationToken);
            var properties = ImmutableDictionary<string, string?>.Empty;
            var additional = new List<Location>();
            if (declaration is not null && IsMovable(declaration, local, call, work, context))
            {
                properties = properties.Add(MovableProperty, "1");
                if (call.TargetMethod.IsGenericMethod)
                {
                    properties = properties.Add(ReturnsProperty, "1");
                }

                additional.Add(declaration.GetLocation());
                additional.Add(work.Syntax.GetLocation());
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask185, references[0].Syntax.GetLocation(), additional, properties, local.Name, local.Type.Name));
        }
    }

    private static bool IsTransaction(IMethodSymbol method, INamedTypeSymbol db) =>
        method is { Name: "Transaction", IsStatic: true } && SymbolEqualityComparer.Default.Equals(method.ContainingType, db);

    // The lambda handed to Db.Transaction as its work.
    private static IAnonymousFunctionOperation? WorkOf(IInvocationOperation call) =>
        call.Arguments
            .Select(static argument => argument.Value)
            .OfType<IDelegateCreationOperation>()
            .Select(static creation => creation.Target)
            .OfType<IAnonymousFunctionOperation>()
            .FirstOrDefault();

    // The work of an outer Db.Transaction this call is written in, or null.
    private static IAnonymousFunctionOperation? Enclosing(IOperation operation, INamedTypeSymbol db)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation function &&
                current.Parent is IDelegateCreationOperation { Parent: IArgumentOperation { Parent: IInvocationOperation outer } } &&
                IsTransaction(outer.TargetMethod, db))
            {
                return function;
            }
        }

        return null;
    }

    private static bool IsAggregate(ITypeSymbol type) => type is INamedTypeSymbol named && AggregateShape.IsAggregate(named.OriginalDefinition);

    private static bool DeclaredOutside(ILocalSymbol local, SyntaxNode work) =>
        local.DeclaringSyntaxReferences.Length > 0 &&
        local.DeclaringSyntaxReferences.All(reference => reference.SyntaxTree != work.SyntaxTree || !work.Span.Contains(reference.Span));

    private static bool IsWritten(ILocalReferenceOperation reference) =>
        reference.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == reference,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } => true,
            _ => false,
        };

    // `var supply = await Supply.Find(id);` — the statement that declares the local, when it declares only it.
    private static LocalDeclarationStatementSyntax? DeclarationOf(ILocalSymbol local, System.Threading.CancellationToken cancellationToken) =>
        local.DeclaringSyntaxReferences.Length == 1 &&
        local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: not null } declarator &&
        declarator.Parent is VariableDeclarationSyntax { Variables.Count: 1, Parent: LocalDeclarationStatementSyntax statement } &&
        statement.Modifiers.Count == 0 && statement.UsingKeyword.RawKind == 0 && statement.AwaitKeyword.RawKind == 0
            ? statement
            : null;

    // The declaration moves into the work as it is written when it sits in the same block, before the statement that
    // runs the transaction; nothing outside the work reads the local; and an await it makes lands in an async lambda.
    private static bool IsMovable(
        LocalDeclarationStatementSyntax declaration, ILocalSymbol local, IInvocationOperation call, IAnonymousFunctionOperation work,
        OperationAnalysisContext context)
    {
        var runs = call.Syntax.FirstAncestorOrSelf<StatementSyntax>();
        if (runs is null || declaration.Parent is not BlockSyntax block || runs.Parent != block ||
            block.Statements.IndexOf(declaration) >= block.Statements.IndexOf(runs) ||
            work.Syntax is not AnonymousFunctionExpressionSyntax lambda ||
            (lambda.AsyncKeyword.RawKind == 0 && declaration.DescendantNodes().Any(static node => node.IsKind(SyntaxKind.AwaitExpression))))
        {
            return false;
        }

        var model = call.SemanticModel;
        var scope = block.Parent ?? block;
        return model is not null && scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => string.Equals(name.Identifier.ValueText, local.Name, System.StringComparison.Ordinal) && !lambda.Span.Contains(name.Span))
            .All(name => !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, context.CancellationToken).Symbol, local));
    }
}
