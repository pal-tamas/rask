using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK038 / RASK039 — the builder surface's half of RASK001.
///     <para>
///         A generated factory makes a non-nullable, no-initializer property a <i>required
///         parameter</i>, so leaving it out is a compile error the language reports for us. A builder
///         chain has no parameters — the same property is set by <c>.Title("…")</c> somewhere along
///         the chain — so nothing enforces it and the component renders with a null it was never
///         supposed to see. This walks the chain and reports what it never named (RASK038).
///     </para>
///     <para>
///         That answer is only sound while the chain is <b>one expression</b>. Assign it to a local
///         and the rest of it can be anywhere, so the analyzer says it cannot tell (RASK039) instead
///         of reporting properties that the next statement may well set.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiredBuilderPropertyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask038 = new(
        "RASK038",
        "Builder chain does not set a required property",
        "Component '{0}' requires {1}, and this builder chain never sets {2}. Add {3} to the chain — or give the property a nullable type or a member initializer if it really is optional.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A non-nullable property with no member initializer is required (the same rule RASK001 "
                     + "describes for the generated factory, where the language enforces it as a missing "
                     + "argument). On the builder surface it is set by a setter in the chain, so an omitted "
                     + "one compiles and leaves the component holding null. The check covers properties "
                     + "declared in this compilation, plus any property marked with the 'required' modifier.",
        helpLinkUri: DiagnosticHelp.Link("RASK038"));

    private static readonly DiagnosticDescriptor Rask039 = new(
        "RASK039",
        "Builder chain is split across statements, so its required properties cannot be checked",
        "The builder chain for '{0}' is stored rather than used here, so Rask cannot tell whether {1} ({2}) is ever set. Keep the chain in a single expression — or set the property before storing it.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "RASK038 reads the chain that follows an entry. Once the chain is assigned to a local "
                     + "or a field the remaining setters can be applied anywhere, including in a branch or "
                     + "another method, so claiming a property is missing would be a guess. This reports the "
                     + "gap in the analysis rather than a wrong answer: the chain still may be complete.",
        helpLinkUri: DiagnosticHelp.Link("RASK039"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask038, Rask039);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentFullName);
            if (component is null)
            {
                return;
            }

            start.RegisterOperationAction(
                ctx => Analyze(ctx, component),
                OperationKind.PropertyReference,
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var operation = context.Operation;
        var member = operation switch
        {
            IPropertyReferenceOperation p => (ISymbol)p.Property,
            IInvocationOperation i => i.TargetMethod,
            _ => null,
        };

        if (member is null || BuilderEntry.EntryTypeOf(member, component) is not { } entryType)
        {
            return;
        }

        // `this.Card = …` and `nameof(Card)` read as an entry too, but neither starts a chain.
        if (operation.Parent is INameOfOperation
            || (operation.Parent is IAssignmentOperation assignment && assignment.Target == operation))
        {
            return;
        }

        var required = BuilderEntry.RequiredProperties(entryType, context.CancellationToken);
        if (required.Count == 0)
        {
            return;
        }

        var (outermost, named) = WalkChain(operation.Syntax);

        // A method entry (a generic component, or a bound form control whose `Bind` argument is what
        // infers the value type) sets what it takes, so its parameters count as named.
        if (operation is IInvocationOperation invocation)
        {
            foreach (var parameter in invocation.TargetMethod.Parameters)
            {
                named.Add(parameter.Name);
            }
        }

        var missing = required.Where(p => !BuilderEntry.NamedBy(p, named)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var names = Quote(missing);
        var setters = string.Join(", ", missing.Select(static p => "'." + p.Name + "(…)'"));
        var location = operation.Syntax.GetLocation();

        context.ReportDiagnostic(IsStored(outermost)
            ? Diagnostic.Create(Rask039, location, entryType.ToDisplayString(),
                missing.Count == 1 ? "a required property" : "every required property", names)
            : Diagnostic.Create(Rask038, location, entryType.ToDisplayString(),
                names, missing.Count == 1 ? "it" : "them", setters));
    }

    /// <summary>
    ///     Follows an entry up through its setter calls, collecting the property names the chain
    ///     mentions and returning the outermost expression the chain produces. Child indexing
    ///     (<c>Div[…]</c>), parentheses and a null-forgiving <c>!</c> are part of the same expression, so
    ///     they pass straight through.
    /// </summary>
    private static (SyntaxNode Outermost, HashSet<string> Named) WalkChain(SyntaxNode entry)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        var node = entry;

        while (true)
        {
            switch (node.Parent)
            {
                case MemberAccessExpressionSyntax access
                    when access.Expression == node && access.Parent is InvocationExpressionSyntax invocation:
                    named.Add(access.Name.Identifier.ValueText);
                    node = invocation;
                    continue;
                case ElementAccessExpressionSyntax element when element.Expression == node:
                    node = element;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when postfix.Operand == node:
                    node = postfix;
                    continue;
                default:
                    return (node, named);
            }
        }
    }

    // The chain outlives this expression: a local/field initializer, or the right-hand side of an
    // assignment. Anything else — an argument, a child, a return, an expression body — consumes it here,
    // which is what makes the walk above complete.
    private static bool IsStored(SyntaxNode outermost) =>
        outermost.Parent switch
        {
            EqualsValueClauseSyntax clause => clause.Value == outermost,
            AssignmentExpressionSyntax assignment => assignment.Right == outermost,
            _ => false,
        };

    private static string Quote(List<IPropertySymbol> properties) =>
        string.Join(", ", properties.Select(static p => "'" + p.Name + "'"));
}
