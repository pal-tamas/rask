using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

// RASK098 — a web API member the app's own browser targets lack. Opt-in: the RaskBrowserTargets property names the
// oldest browser of each engine the app supports ("safari >= 16; firefox >= 115"), and a call to a generated member
// whose MDN support (the BrowserSupport attribute the emitter writes beside the doc comment) starts later, or never,
// is reported where it is made. A call under an IsSupported guard is the app handling it, and is not.
// RASK099 — an entry of that property this analyzer cannot read, once per build, so a typo does not silently check
// nothing.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BrowserTargetsAnalyzer : DiagnosticAnalyzer
{
    private const string TargetsProperty = "build_property.RaskBrowserTargets";
    private const string SupportAttributeName = "BrowserSupportAttribute";
    private const string SupportAttribute = "Rask.Core." + SupportAttributeName;
    private const string Guard = "IsSupported";

    private static readonly DiagnosticDescriptor Rask098 = new(
        "RASK098",
        "Web API member is missing from a browser target",
        "'{0}' is not in {1} — guard it with 'if (await ….IsSupported)', or raise <RaskBrowserTargets>",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "MDN's browser data says a browser <RaskBrowserTargets> names does not ship this member, or ships it only "
        + "from a later version. Ask the browser first with IsSupported and give that browser another path, or raise "
        + "the target if the app no longer supports that version.",
        DiagnosticHelp.Link("RASK098"));

    private static readonly DiagnosticDescriptor Rask099 = new(
        "RASK099",
        "Browser target not understood",
        "<RaskBrowserTargets> entry '{0}' is not '<browser> >= <version>' with a browser of chrome, edge, firefox or safari",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Each ';'-separated entry of <RaskBrowserTargets> is a browser (chrome, edge, firefox, safari), '>=', and the "
        + "oldest version the app supports, as in 'safari >= 16'. An entry that is not is checked against nothing.",
        DiagnosticHelp.Link("RASK099"),
        WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask098, Rask099);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(TargetsProperty, out var text)
                || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var targets = BrowserTarget.Parse(text, out var unread);
            if (unread.Count > 0)
            {
                start.RegisterCompilationEndAction(end =>
                {
                    foreach (var entry in unread)
                    {
                        end.ReportDiagnostic(Diagnostic.Create(Rask099, Location.None, entry));
                    }
                });
            }

            if (targets.Count == 0)
            {
                return;
            }

            start.RegisterOperationAction(
                ctx => Analyze(ctx, ((IInvocationOperation)ctx.Operation).TargetMethod, targets),
                OperationKind.Invocation);
            start.RegisterOperationAction(
                ctx => Analyze(ctx, ((IPropertyReferenceOperation)ctx.Operation).Property, targets),
                OperationKind.PropertyReference);
            // An attribute's keyword a target lacks (`Popover.Hint`): an enum member, read as a field.
            start.RegisterOperationAction(
                ctx => Analyze(ctx, ((IFieldReferenceOperation)ctx.Operation).Field, targets),
                OperationKind.FieldReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context, ISymbol member, IReadOnlyList<BrowserTarget> targets)
    {
        var attribute = SupportOf(member);
        if (attribute is null)
        {
            return;
        }

        var missing = targets.Select(t => t.Lacks(attribute)).OfType<string>().ToList();
        if (missing.Count == 0 || IsInsideNameof(context.Operation) || IsGuarded(context.Operation.Syntax))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask098, NameLocation(context.Operation.Syntax), member.Name, string.Join(", ", missing)));
    }

    // The attribute on the member as called: a classic extension's reduced form and a generic's construction both
    // carry it on their definition. Matched by name: the attribute is internal to Rask.Core, so no compilation but
    // Rask.Core's own can name the type.
    private static AttributeData? SupportOf(ISymbol member)
    {
        var definition = member is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : member.OriginalDefinition;
        return definition.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { Name: SupportAttributeName } type
            && string.Equals(type.ToDisplayString(), SupportAttribute, StringComparison.Ordinal));
    }

    private static bool IsInsideNameof(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    // Under a check that asked the browser: the body of `if (await X.IsSupported)` (with `&&` alongside), the right of
    // `await X.IsSupported && …`, a `? :`'s true branch, or a statement after `if (!await X.IsSupported) return;`. Any
    // IsSupported counts, not only the same receiver's: what an app reaches from a guarded object (the device a
    // supported navigator.usb hands back) needs no guard of its own.
    private static bool IsGuarded(SyntaxNode node)
    {
        var child = node;
        for (var parent = node.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            var guarded = parent switch
            {
                IfStatementSyntax s => s.Statement == child && Asks(s.Condition),
                ConditionalExpressionSyntax c => c.WhenTrue == child && Asks(c.Condition),
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => b.Right == child && Asks(b.Left),
                BlockSyntax block when child is StatementSyntax statement => AfterAnEarlyExit(block, statement),
                _ => false,
            };
            if (guarded)
            {
                return true;
            }

            if (parent is MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool AfterAnEarlyExit(BlockSyntax block, StatementSyntax statement) =>
        block.Statements.TakeWhile(s => s != statement).Any(s =>
            s is IfStatementSyntax { Else: null } check
            && Unwrap(check.Condition) is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not
            && Asks(not.Operand)
            && Exits(check.Statement));

    private static bool Exits(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax => true,
        BlockSyntax { Statements.Count: > 0 } block => Exits(block.Statements[block.Statements.Count - 1]),
        _ => false,
    };

    private static bool Asks(ExpressionSyntax condition) => Unwrap(condition) switch
    {
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => Asks(b.Left) || Asks(b.Right),
        MemberAccessExpressionSyntax access => string.Equals(access.Name.Identifier.ValueText, Guard, StringComparison.Ordinal),
        _ => false,
    };

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax p:
                    expression = p.Expression;
                    break;
                case AwaitExpressionSyntax a:
                    expression = a.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }

    private static Location NameLocation(SyntaxNode syntax) => syntax switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax m } => m.Name.GetLocation(),
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => syntax.GetLocation(),
    };
}
