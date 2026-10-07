using Rask.Core.Forms;

namespace Rask;

// What sits at the end of the box: the shortcut, the clear and reveal buttons, the trailing icon.
public sealed partial class UiInput<T>
{
    private bool _viewing;

    private Component? Trailing()
    {
        Component?[] parts =
        [
            Kbd is { } kbd ? Span.Class("pe-2")[kbd] : null,
            ShowsClear ? ClearButton() : null,
            Viewable == true ? ViewButton() : null,
            IconTrailing is { } icon ? Glyph(icon) : null,
        ];

        return parts.All(part => part is null) ? null : Div.Class(UiInputLook.Trailing)[parts];
    }

    // Always rendered and hidden by CSS while the input is empty, so it appears with the first keystroke
    // rather than with the next round trip. Out of the tab order, as Flux's is: Backspace clears too.
    private Component ClearButton() =>
        ActionButton("Clear input", "ui-clear-button", marked: true)
            .Class(UiClass.Compose(UiInputLook.Action, UiInputLook.Clear))
            .TabIndex(-1)
            .OnClick(Clear)[
            Ui.Icon.Name(Ui.IconName.XMark).Mini
        ];

    private Component ViewButton() =>
        ActionButton("Toggle password visibility", "viewable-open", _viewing)
            .Class(UiInputLook.Action)
            .OnClick(() => _viewing = !_viewing)[
            Ui.Icon.Name(Ui.IconName.EyeSlash).Mini.Class(_viewing ? null : "hidden"),
            Ui.Icon.Name(Ui.IconName.Eye).Mini.Class(_viewing ? "hidden" : null)
        ];

    // One data bag per button: a second .Data call would replace the first.
    private static HTMLButtonElement ActionButton(string label, string mark, bool marked)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-button"] = string.Empty };
        if (marked)
        {
            data[mark] = string.Empty;
        }

        return Button.Type(ButtonType.Button).Aria("label", label).Data(data);
    }

    private async Task Clear()
    {
        // An empty string rather than null where the input holds text: a non-nullable member stays one.
        var empty = typeof(T) == typeof(string) ? (T)(object)string.Empty : default!;
        if (Bind is not { } bind)
        {
            await OnChange.Invoke(empty).ConfigureAwait(false);
            return;
        }

        var accessor = ExpressionAccessor.Parse(bind);
        accessor.Setter(empty);
        await BindingHelpers
            .NotifyAndValidateField(BindingHelpers.ResolveBindingContext(accessor.Target), accessor.Field)
            .ConfigureAwait(false);
        await AfterBind.Invoke(empty).ConfigureAwait(false);
    }
}
