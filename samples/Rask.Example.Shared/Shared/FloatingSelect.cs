using System.Linq.Expressions;

namespace Rask.Example.Shared;

// Bootstrap 5.3 floating-label <select> — the sibling of FloatingInput for dropdowns
// (https://getbootstrap.com/docs/5.3/forms/floating-labels/#selects). Same contract: no validation
// state of its own, no extra CSS, label from the bound property's [Display(Name)]. The <option>s are
// passed as children, so include an empty first option for the label to float over when nothing is
// selected:
//
//   FloatingSelect(() => model.Plan)[Option("")["— choose —"], Option("pro")["Pro"]]
public sealed partial class FloatingSelect<TProp> : Component
{
    public required Expression<Func<TProp>> Bind { get; set; }

    protected override Component? Render()
    {
        var (id, label) = FloatingField.Resolve(Bind);
        return Div(Class: "form-floating mb-3")[
            // form-select (not form-control); the caller's <option>s flow in as Children.
            Select(Bind).Id(id).Class("form-select")[Children ?? Array.Empty<Component>()],
            Label(id)[label],
            ValidationMessage(Bind, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]])
        ];
    }
}
