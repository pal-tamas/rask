using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     What a delete does to a relationship the model leaves to convention, as RASK192 reads it off a compilation: Rask's
///     own rule (a child cascades from its aggregate, every other key is EF Core's), or the app's
///     <c>c.DeleteBehavior(children: …, others: …)</c> where it says one.
/// </summary>
/// <param name="Children">What a child's key to its aggregate does: <c>Cascade</c> unless the app says otherwise.</param>
/// <param name="Others">What every other key does, when the app says; null leaves it to EF Core (<see cref="Cascade" /> for a required key, <see cref="ClientSetNull" /> for an optional one).</param>
/// <param name="Cascade"><c>DeleteBehavior.Cascade</c>'s value.</param>
/// <param name="ClientSetNull"><c>DeleteBehavior.ClientSetNull</c>'s value.</param>
internal sealed record DeleteRule(int Children, int? Others, int Cascade, int ClientSetNull)
{
    private const string DeleteBehavior = "DeleteBehavior";

    /// <summary>
    ///     The rule <paramref name="compilation" /> follows, or null where it cannot be read — no EF Core, or a
    ///     <c>DeleteBehavior(…)</c> call whose arguments are not <c>DeleteBehavior.X</c> constants, or two that differ.
    /// </summary>
    /// <remarks>Read off the syntax, once per compilation: an analyzer may not ask for another tree's semantic model.</remarks>
    public static DeleteRule? Of(Compilation compilation, CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore." + DeleteBehavior) is not { } behavior ||
            Value(behavior, "Cascade") is not { } cascade || Value(behavior, "ClientSetNull") is not { } clientSetNull)
        {
            return null;
        }

        var stated = new HashSet<(int Children, int Others)>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!tree.GetText(cancellationToken).ToString().Contains("." + DeleteBehavior + "("))
            {
                continue;
            }

            foreach (var call in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(static i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: DeleteBehavior }))
            {
                if (Stated(call, behavior) is not { } rule)
                {
                    return null;
                }

                stated.Add(rule);
            }
        }

        return stated.Count switch
        {
            0 => new DeleteRule(cascade, null, cascade, clientSetNull),
            1 => new DeleteRule(stated.First().Children, stated.First().Others, cascade, clientSetNull),
            _ => null,
        };
    }

    // `c.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction)`, by name or by position.
    private static (int Children, int Others)? Stated(InvocationExpressionSyntax call, INamedTypeSymbol behavior)
    {
        if (call.ArgumentList.Arguments.Count != 2)
        {
            return null;
        }

        int? children = null;
        int? others = null;
        for (var i = 0; i < 2; i++)
        {
            var argument = call.ArgumentList.Arguments[i];
            if (argument.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: DeleteBehavior } } member ||
                Value(behavior, member.Name.Identifier.ValueText) is not { } value)
            {
                return null;
            }

            var name = argument.NameColon?.Name.Identifier.ValueText ?? (i == 0 ? "children" : "others");
            if (string.Equals(name, "children", System.StringComparison.Ordinal))
            {
                children = value;
            }
            else
            {
                others = value;
            }
        }

        return children is { } c && others is { } o ? (c, o) : null;
    }

    private static int? Value(INamedTypeSymbol behavior, string member) =>
        behavior.GetMembers(member).OfType<IFieldSymbol>().FirstOrDefault() is { HasConstantValue: true, ConstantValue: int value } ? value : null;
}
