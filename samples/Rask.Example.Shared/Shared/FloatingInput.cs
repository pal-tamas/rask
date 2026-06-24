using System.Linq.Expressions;

namespace Rask.Example.Shared;

// A reusable Bootstrap 5.3 floating-label text input, built only from Rask's public form
// primitives — Input + Label + ValidationMessage wrapped in Bootstrap's `.form-floating` markup
// (https://getbootstrap.com/docs/5.3/forms/floating-labels/). It owns no validation state and needs
// no extra CSS: the framework's ValidationMessage reads the EditContext, and the error text uses
// Bootstrap's own `.invalid-feedback .d-block` utilities so it shows without an `.is-invalid` toggle.
// Pairs with any validator dropped into the surrounding Form — DataAnnotationsValidator(),
// FluentValidationValidator(…), or a per-field Validate:.
//
// Generic so the typed Bind expression can be handed straight to Input(Bind, …) in Render; TProp is
// inferred from the lambda at the call site, e.g. FloatingInput(() => model.Name, "Name").
public sealed class FloatingInput<TProp> : Component
{
    public required Expression<Func<TProp>> Bind { get; set; }
    public required string LabelText { get; set; }

    // Derived from the bound property name so <label for> matches <input id> without a manual id.
    private string Id => "ff-" + FieldName(Bind);

    // Input infers its type from TProp (text/number/date/…). Bootstrap floating labels REQUIRE a
    // placeholder. ValidationMessage renders nothing until the field has messages; `d-block` forces
    // the feedback visible without an `.is-invalid` sibling toggle.
    protected override RenderResult Render() =>
        Div(Class: "form-floating mb-3")[
            Input(Bind, Id: Id, Placeholder: LabelText, Class: "form-control"),
            Label(Id)[LabelText],
            ValidationMessage(Bind, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]])
        ];

    // Public System.Linq.Expressions reflection only — no EditContext, no internal Rask APIs.
    private static string FieldName(LambdaExpression expr)
    {
        var body = expr.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : expr.Body;
        return body is MemberExpression m
            ? m.Member.Name
            : throw new ArgumentException(
                $"FloatingInput.Bind must be a property access, e.g. () => model.Name. Got: {expr}", nameof(expr));
    }
}
