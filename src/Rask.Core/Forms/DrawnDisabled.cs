namespace Rask.Core.Forms;

// Whether a form control is drawn disabled (IFormControlElement.DrawnDisabled): its typed Disabled, or a `disabled`
// written through the Attributes escape hatch, which the browser reads just the same.
internal static class DrawnDisabled
{
    /// <summary>Whether <paramref name="element" /> is drawn disabled, its own <c>Disabled</c> being <paramref name="disabled" />.</summary>
    internal static bool Is(bool? disabled, Component element) =>
        disabled is true || element.ExtraAttrsInternal?.ContainsKey("disabled") == true;

    /// <summary>The name a bound or named control posts under: its own <c>Name</c>, else the bound member's.</summary>
    internal static string? PostsUnder(string? name, System.Linq.Expressions.LambdaExpression? bind) =>
        name ?? (bind is null ? null : ExpressionAccessor.Parse(bind).PropertyName);
}
