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
        return Div.Class($"{Tw.FormFloating} mb-3")[
            // form-select (not form-control); the caller's <option>s flow in as Children.
            Select.Bind(Bind).Id(id).Class(Tw.Select)[Children ?? Array.Empty<Component>()],
            Label.For(id)[label],
            ValidationMessage.Template(msgs => Div.Class("field-error mt-1 text-sm text-ui-danger")[msgs[0]]).For(Bind)
        ];
    }
}
