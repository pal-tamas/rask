using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

public sealed class ValidationMessage : Component
{
    // Class is inherited from Component — repurposed here as "class to apply to each rendered
    // message Div" since ValidationMessage itself emits no element tag (TagName is null).
    public LambdaExpression? For { get; set; }

    protected override Component Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null || For is null)
        {
            return new Fragment();
        }

        var acc = ExpressionAccessor.Parse(For);
        var msgs = ctx.GetValidationMessages(acc.Field);
        if (msgs.Count == 0)
        {
            return new Fragment();
        }

        var children = msgs.Select(m => (Child)Components.Div(Class: Class ?? "validation-message", Children: [m]));
        return new Fragment(children.ToArray());
    }
}

public sealed class ValidationSummary : Component
{
    // Class inherited from Component — applied to the rendered <ul>.
    protected override Component Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null)
        {
            return new Fragment();
        }

        var msgs = ctx.GetValidationMessages().ToList();
        if (msgs.Count == 0)
        {
            return new Fragment();
        }

        var items = msgs.Select(m => (Child)Components.Li(Children: [m])).ToArray();
        return Components.Ul(Class: Class ?? "validation-summary", Children: items);
    }
}
