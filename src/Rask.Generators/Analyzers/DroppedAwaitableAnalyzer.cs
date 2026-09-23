using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK093 — an awaitable used as a statement and never awaited, in a method that is not async.
//
// Rask's timing steps are awaitable builders that do nothing until awaited: `await Mail.Send(email)`
// queues the mail, `Mail.Send(email);` queues nothing and says nothing. Inside an ASYNC method the
// compiler already refuses that (CS4014, an error here because the build is -warnaserror), so this
// analyzer covers the half it leaves open — a sync method, where the same line compiles clean and the
// mail is simply never sent. That silence is the whole problem: no exception, no log, no mail.
//
// It tests the SHAPE (does the type have a usable GetAwaiter?) rather than a list of Rask's own builder
// types, so the seventh builder is covered the day it is written, and an app's own awaitable is too.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DroppedAwaitableAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask093 = new(
        "RASK093",
        "Awaitable result is dropped",
        "'{0}' does nothing until it is awaited — write 'await {0}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "A call that returns something awaitable has not run when it returns: awaiting it is what runs "
        + "it. Dropped in a method that is not async, it compiles, does nothing, and reports nothing. "
        + "Await it, or assign it if you mean to await it later.",
        DiagnosticHelp.Link("RASK093"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask093);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.ExpressionStatement,
            SyntaxKind.ArrowExpressionClause,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        // Three shapes drop a value:
        //
        //   Mail.Send(email);                        a statement
        //   void Register() => Mail.Send(email);     an expression body returning void
        //   .OnClick(() => Mail.Send(email))         a lambda BOUND TO A VOID DELEGATE
        //
        // The third is the one that matters most, and the only one with no warning of any kind today.
        // An event prop offers both Action and Func<Task> overloads; a lambda whose body is a builder
        // binds to Action — the compiler is content, the value is discarded, and the mail is never sent.
        // It is also the most natural line anyone would write in a component.
        var expression = context.Node switch
        {
            ExpressionStatementSyntax statement => statement.Expression,
            ArrowExpressionClauseSyntax arrow when ReturnsNothing(context, arrow) => arrow.Expression,
            AnonymousFunctionExpressionSyntax lambda when BoundToVoid(context, lambda) =>
                lambda.ExpressionBody,
            _ => null,
        };

        // Only a call can be dropped this way; an assignment keeps the value, and `await x;` awaited it.
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        // CS4014 owns the async case, and reporting both would put two messages on one line.
        if (IsInsideAsync(context.Node))
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(invocation, context.CancellationToken).Type is not { } type
            || type.SpecialType == SpecialType.System_Void
            || !IsAwaitable(type))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask093, invocation.GetLocation(), invocation.ToString()));
    }

    /// <summary>
    /// Whether this lambda was converted to a delegate that returns nothing, so whatever its body
    /// produces is thrown away. <c>Func&lt;Task&gt;</c> hands the task to its caller and is not a drop;
    /// <c>Action</c> is.
    /// </summary>
    private static bool BoundToVoid(SyntaxNodeAnalysisContext context, AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda.ExpressionBody is null || lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
        {
            return false;
        }

        return context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType
            is INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: true };
    }

    /// <summary>
    /// Whether the member this arrow body belongs to returns nothing, so its value is discarded. A
    /// member that returns the call's result — <c>Task Register() =&gt; Mail.SendNow(x);</c> — hands it to
    /// its caller to await, which is not a drop.
    /// </summary>
    private static bool ReturnsNothing(SyntaxNodeAnalysisContext context, ArrowExpressionClauseSyntax arrow)
    {
        var symbol = arrow.Parent is null
            ? null
            : context.SemanticModel.GetDeclaredSymbol(arrow.Parent, context.CancellationToken);

        return symbol is IMethodSymbol { ReturnsVoid: true };
    }

    /// <summary>
    /// The nearest enclosing method, lambda or local function — not the outermost, because a sync local
    /// function inside an async method is still sync, and that is exactly where this slips through.
    /// </summary>
    private static bool IsInsideAsync(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AnonymousFunctionExpressionSyntax lambda:
                    return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case AccessorDeclarationSyntax accessor:
                    return accessor.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }
        }

        return false;
    }

    /// <summary>
    /// Awaitable by the compiler's own rule: a <c>GetAwaiter()</c> taking no arguments whose result
    /// carries <c>IsCompleted</c>, <c>GetResult()</c> and <c>OnCompleted</c>. Checked structurally, so it
    /// holds for Task, ValueTask, Rask's builders and anything an app writes.
    /// </summary>
    private static bool IsAwaitable(ITypeSymbol type)
    {
        var getAwaiter = type.GetMembers("GetAwaiter")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == 0 && !m.ReturnsVoid);

        // An extension GetAwaiter is legal C#; it is also vanishingly rare, and finding one needs the
        // whole compilation's extension methods. Missing it costs a diagnostic, never a false one.
        if (getAwaiter?.ReturnType is not { } awaiter)
        {
            return false;
        }

        return awaiter.GetMembers("IsCompleted").OfType<IPropertySymbol>().Any(p => p.GetMethod is not null)
            && awaiter.GetMembers("GetResult").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0)
            && awaiter.AllInterfaces.Any(i => i.Name == "INotifyCompletion");
    }
}
