using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Tools.BuilderRewrite;

/// <summary>
///     Moves a chain's required steps to the front, which is where the state machine now insists they go.
/// </summary>
/// <remarks>
///     <para>
///         A step is reachable only from the state before it, so <c>BsIcon.Class("x").Name(icon)</c> no
///         longer compiles: <c>Class</c> is a setter on the finished component, and the component does not
///         exist until <c>Name</c> has been given. The fix is purely an ordering one — every call and
///         every argument survives, so this reorders rather than rewrites.
///     </para>
///     <para>
///         Relative order is preserved WITHIN each group. Two required steps keep the order they were
///         written in (the machine accepts any), and the setters that follow keep theirs, because a
///         chain's setters can be order-dependent in ways this cannot see — two writes to the same
///         property, or a `Class` that appends.
///     </para>
/// </remarks>
internal sealed class ChainReorderer(SurfaceModel surface, bool reflow = false) : CSharpSyntaxRewriter
{
    private readonly bool _reflow = reflow;

    private readonly SurfaceModel _surface = surface;

    private SemanticModel _model = null!;

    public int Reordered { get; private set; }

    public SyntaxNode Run(SemanticModel model, SyntaxNode root)
    {
        _model = model;
        return Visit(root);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Outermost first: the whole chain is rebuilt in one go, so descending into it afterwards would
        // reorder the pieces of something already correct.
        if (node.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return base.VisitInvocationExpression(node);
        }

        var calls = new List<(string Name, ArgumentListSyntax Args)>();
        var current = (ExpressionSyntax)node;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
        {
            calls.Add((access.Name.Identifier.ValueText, call.ArgumentList));
            current = access.Expression;
        }

        // `Input<string>()` — the parameterless opener a generic METHOD entry used to need. There is no
        // such entry now: the seed is a property and a step pins the type, so the opener is both dead and
        // unnecessary, and dropping it is part of putting the chain in order.
        var droppedOpener = false;
        if (current is InvocationExpressionSyntax
            {
                Expression: GenericNameSyntax opener, ArgumentList.Arguments.Count: 0,
            })
        {
            current = SyntaxFactory.IdentifierName(opener.Identifier);
            droppedOpener = true;
        }

        if (calls.Count < 1 || current is not SimpleNameSyntax seedName)
        {
            return base.VisitInvocationExpression(node);
        }

        var steps = _surface.StepNamesFor(_model, node.SpanStart, seedName.Identifier.ValueText);
        if (steps.Count == 0)
        {
            return base.VisitInvocationExpression(node);
        }

        calls.Reverse();

        // The OPENING first among the steps: it is the one the seed itself offers, and every other step
        // is reachable only from the state it produces. A chain that named a later step first —
        // `BsCheckboxGroup.Options(o).Value(v)` — is not merely out of order, it chose no mode at all.
        var openings = _surface.OpeningNamesFor(_model, node.SpanStart, seedName.Identifier.ValueText);
        var leading = calls.Where(c => steps.Contains(c.Name))
            .OrderBy(c => openings.Contains(c.Name) ? 0 : 1)
            .ToList();
        var trailing = calls.Where(c => !steps.Contains(c.Name)).ToList();

        // Already in order — and a chain with no steps at all is somebody else's business. `reflow`
        // rebuilds anyway, which is how a chain this tool itself flattened on an earlier run gets its
        // line breaks back.
        // A dead opener comes off whether or not the rest is in order — it is the reason the site does
        // not compile. But ONLY when the chain has a step to open with instead: `Input<string>()` with
        // nothing but setters after it has no pin at all, and dropping its opener turns a site that
        // needs a decision into one that silently names no type. Those are left for a human.
        var ordered = calls.Take(leading.Count).SequenceEqual(leading);
        if ((!droppedOpener || leading.Count == 0)
            && (leading.Count == 0
                || (ordered && !(_reflow && node.ToString().Length > 116 && !node.ToString().Contains('\n')))))
        {
            return base.VisitInvocationExpression(node);
        }

        Reordered++;
        return SyntaxFactory.ParseExpression(Format(node, seedName.Identifier.ValueText, leading, trailing))
            .WithTriviaFrom(node);
    }

    // Rebuilt as TEXT rather than as syntax, for the same reason the site rewriter does it: a chain
    // assembled from nodes comes out on one line, and a reordered chain is exactly the case where the
    // line was already long. One call per line, indented one step past where the chain starts, whenever
    // the original was multi-line or the flat form would overrun.
    private static string Format(
        SyntaxNode node, string root,
        List<(string Name, ArgumentListSyntax Args)> leading,
        List<(string Name, ArgumentListSyntax Args)> trailing)
    {
        var calls = leading.Concat(trailing)
            .Select(c => "." + c.Name + c.Args.WithoutTrivia().ToFullString().Trim())
            .ToList();

        var indent = IndentOf(node);
        var single = root + string.Concat(calls);
        return node.ToString().Contains('\n') || indent.Length + single.Length > 116
            ? root + string.Concat(calls.Select(c => "\n" + indent + "    " + c))
            : single;
    }

    // The whitespace that opens the line the chain starts on; `dotnet format` normalises from there.
    private static string IndentOf(SyntaxNode node)
    {
        var text = node.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(node.SpanStart).ToString();
        return line[..(line.Length - line.TrimStart().Length)];
    }
}
