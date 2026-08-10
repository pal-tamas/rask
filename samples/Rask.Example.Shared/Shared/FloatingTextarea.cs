using System.Linq.Expressions;

namespace Rask.Example.Shared;

// Bootstrap 5.3 floating-label <textarea> — the sibling of FloatingInput for multi-line text
// (https://getbootstrap.com/docs/5.3/forms/floating-labels/#textareas). Same contract: no validation
// state of its own, no extra CSS, label from the bound property's [Display(Name)]. Floating-label
// textareas need a placeholder and a fixed height for the label to sit correctly, so a default
// height is applied.
public sealed partial class FloatingTextarea<TProp> : Component
{
    public required Expression<Func<TProp>> Bind { get; set; }

    protected override Component? Render()
    {
        var (id, label) = FloatingField.Resolve(Bind);
        return Div.Class("form-floating mb-3")[
            Textarea(Bind).Id(id).Placeholder(label).Class("form-control").Style("height: 6rem"),
            Label.For(id)[label],
            ValidationMessage(Bind, msgs => Div.Class("invalid-feedback d-block")[msgs[0]])
        ];
    }
}
