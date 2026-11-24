using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask;

// The bool a field's Clear is bound to, on the same model as the field: `() => model.NickCleared`, built for the checkbox.
internal static class UiClearedBy
{
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the runtime type of a form's model, which the form keeps whole: a model type is " +
                        "DAM-annotated where it enters the form (HTMLFormElement<TModel>), as EditContext reads it.")]
    internal static Expression<Func<bool?>> Of(object model, string property)
    {
        var cleared = model.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
                      ?? throw new InvalidOperationException(
                          $"{model.GetType().Name} has no public bool {property}, which its Clear is registered to.");
        // As a bound checkbox takes a bool: through bool?, the way a bool member is bound (UiBoundControl).
        return Expression.Lambda<Func<bool?>>(Expression.Convert(Expression.Property(Expression.Constant(model), cleared), typeof(bool?)));
    }
}
