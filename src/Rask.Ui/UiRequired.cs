using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Whether the member a control is bound to must be filled: the one question, put to
///     <see cref="RaskValidation.IsRequired" />, behind every <c>aria-required</c> the kit writes.
/// </summary>
internal static class UiRequired
{
    /// <summary>Asks for the member <paramref name="bound" /> ends in, on the declared type of what owns it.</summary>
    /// <param name="bound">The control's bind expression, or null for a control bound to nothing.</param>
    // The declared type, so nothing is evaluated and nothing is built on a render.
    internal static bool Is(LambdaExpression? bound)
    {
        // Under the Convert a bind over a nullable or a concrete collection is lifted through.
        var body = bound?.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } lifted)
        {
            body = lifted.Operand;
        }

        return body is MemberExpression { Expression.Type: var owner } member
               && RaskValidation.IsRequired(owner, member.Member.Name);
    }
}
