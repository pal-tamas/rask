using System.Linq.Expressions;
using Rask.Core.Forms;
using static Rask.Core.Tags;

namespace Rask.Core.Components;

public sealed class ValidationMessage : Component
{
    private readonly string? _class;
    private readonly LambdaExpression? _for;

    public ValidationMessage(LambdaExpression? @for, string? @class = null)
    {
        _for = @for;
        _class = @class;
    }

    protected override Component Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null || _for is null)
        {
            return new Fragment();
        }

        var acc = ExpressionAccessor.Parse(_for);
        var msgs = ctx.GetValidationMessages(acc.Field);
        if (msgs.Count == 0)
        {
            return new Fragment();
        }

        var children = msgs.Select(m => (Child)Div(Class: _class ?? "validation-message", Children: [m]));
        return new Fragment(children.ToArray());
    }
}

public sealed class ValidationSummary : Component
{
    private readonly string? _class;

    public ValidationSummary(string? @class = null) => _class = @class;

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

        var items = msgs.Select(m => (Child)Li(Children: [m])).ToArray();
        return Ul(Class: _class ?? "validation-summary", Children: items);
    }
}
