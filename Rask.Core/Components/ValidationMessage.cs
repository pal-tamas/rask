using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

public sealed class ValidationMessage : Component
{
    // Class is inherited from Component — repurposed here as "class to apply to each rendered
    // message Div" since ValidationMessage itself emits no element tag (TagName is null).
    public LambdaExpression? For { get; set; }

    // Type-narrowed factory: callers pass `For: () => model.Property` and get an
    // `Expression<Func<TProp>>` checked at compile time, instead of the raw
    // `LambdaExpression?` the property accepts at runtime.
    [GenerateForwarderFactory]
    public static ValidationMessage Bound<TProp>(
        Expression<Func<TProp>> For,
        string? Class = null) => new() { For = For, Class = Class };

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

        var children = msgs.Select(m => (Child)Components.Div(Class: Class ?? "validation-message")[m]);
        return new Fragment(children.ToArray());
    }
}

public sealed class ValidationSummary : Component
{
    // Class is inherited from Component — applied to the rendered <ul>. The generated
    // factory exposes Id/Class/Style/Data; no extra forwarder is needed.

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

        var items = msgs.Select(m => (Child)Components.Li()[m]).ToArray();
        return Components.Ul(Class: Class ?? "validation-summary")[items];
    }
}
