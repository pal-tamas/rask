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
// inferred from the lambda at the call site. The label comes from the bound property's
// [Display(Name)] attribute (falling back to the property name), so a field is just:
//   FloatingInput(() => model.Name)
public sealed class FloatingInput<TProp> : Component
{
    public required Expression<Func<TProp>> Bind { get; set; }

    // Input infers its type from TProp (text/number/date/…). Bootstrap floating labels REQUIRE a
    // placeholder. ValidationMessage renders nothing until the field has messages; `d-block` forces
    // the feedback visible without an `.is-invalid` sibling toggle.
    protected override RenderResult Render()
    {
        var (id, label) = FloatingField.Resolve(Bind);
        return Div(Class: "form-floating mb-3")[
            Input(Bind, Id: id, Placeholder: label, Class: "form-control"),
            Label(id)[label],
            ValidationMessage(Bind, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]])
        ];
    }
}
