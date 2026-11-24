using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// One `rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200)` RASK130's fix rewrites: the RuleFor it starts from, the
// outermost call it ends with, and the calls on it that stay. With no check left among them, the whole rule goes.
internal sealed class FluentChainEdit(
    InvocationExpressionSyntax ruleFor,
    ExpressionSyntax outermost,
    IReadOnlyList<InvocationExpressionSyntax> kept,
    bool removesTheRule)
{
    public InvocationExpressionSyntax RuleFor { get; } = ruleFor;

    public ExpressionSyntax Outermost { get; } = outermost;

    public IReadOnlyList<InvocationExpressionSyntax> Kept { get; } = kept;

    public bool RemovesTheRule { get; } = removesTheRule;
}
