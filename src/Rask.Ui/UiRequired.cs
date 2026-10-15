using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Whether the member a control is bound to must be filled: the one question, put to
///     <see cref="RaskValidation.IsRequired(LambdaExpression)" />, behind every <c>aria-required</c> the kit writes.
/// </summary>
internal static class UiRequired
{
    /// <summary>
    ///     Asks for the member <paramref name="bound" /> ends in — and for what it walks through to get there,
    ///     since nothing under a part of the model that may be left empty has to be filled.
    /// </summary>
    /// <param name="bound">The control's bind expression, or null for a control bound to nothing.</param>
    // Read from the expression's declared types, so nothing is evaluated and nothing is built on a render.
    internal static bool Is(LambdaExpression? bound) => bound is not null && RaskValidation.IsRequired(bound);
}
