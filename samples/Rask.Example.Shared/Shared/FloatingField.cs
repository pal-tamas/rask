using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Example.Shared;

// Shared by FloatingInput / FloatingSelect / FloatingTextarea: resolves the bound property once into
// the control id (ff-{Name}) and its label. The label is the property's [Display(Name)] when present,
// otherwise the property name. Public System.Linq.Expressions/reflection only — no EditContext, no
// internal Rask APIs.
internal static class FloatingField
{
    public static (string Id, string Label) Resolve(LambdaExpression bind)
    {
        var body = bind.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : bind.Body;
        if (body is not MemberExpression { Member: PropertyInfo prop })
        {
            throw new ArgumentException(
                $"Bind must be a property access, e.g. () => model.Name. Got: {bind}", nameof(bind));
        }

        var label = prop.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? prop.Name;
        return ("ff-" + prop.Name, label);
    }
}
