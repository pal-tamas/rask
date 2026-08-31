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
        return Div.Class($"{Ui.FormFloating} mb-3")[
            Textarea.Bind(Bind).Id(id).Placeholder(label).Class(Ui.Input).Style("height: 6rem"),
            Label.For(id)[label],
            ValidationMessage.Template(msgs => Div.Class("field-error mt-1 text-sm text-red-600 dark:text-red-400")[msgs[0]]).For(Bind)
        ];
    }
}
