using System.Linq.Expressions;

namespace Rask;

/// <summary>
///     What a form control tells the <see cref="UiField" /> around it, so the label, the description and the
///     error find it without the call site naming an id.
/// </summary>
internal interface IUiFieldControl
{
    /// <summary>The id the control writes on its own element.</summary>
    string ControlId { get; }

    /// <summary>The model member the control is bound to, whose messages <see cref="UiError" /> shows.</summary>
    LambdaExpression? Bound { get; }
}
