namespace Rask;

// What sits at the end of the box: the shortcut, the clear, copy and reveal buttons, the trailing icon.
public sealed partial class UiInput<T>
{
    private bool _viewing;

    private Component? Trailing(string inputId)
    {
        Component?[] parts =
        [
            Kbd is { } kbd ? Span.Class("pe-2")[kbd] : null,
            ShowsClear ? ClearButton(inputId) : null,
            Copyable == true ? CopyButton(inputId) : null,
            Viewable == true ? ViewButton() : null,
            IconTrailing is { } icon ? Glyph(icon) : null,
        ];

        return parts.All(part => part is null) ? null : Div.Class(UiInputLook.Trailing)[parts];
    }

    // Always rendered and hidden by CSS while the input is empty, so it appears with the first keystroke
    // rather than with the next round trip. Out of the tab order, as Flux's is: Backspace clears too.
    // The runtime empties the field in the click (`data-rask-clear`), tells the page with a real `input` and
    // `change`, and leaves the focus in the field — which is what Flux's script does.
    private static Component ClearButton(string inputId) =>
        ActionButton("Clear input", ("ui-clear-button", string.Empty), ("rask-clear", inputId))
            .Class(UiClass.Compose(UiInputLook.Action, UiInputLook.Clear))
            .TabIndex(-1)[
            Ui.Icon.Name(Ui.IconName.XMark).Mini
        ];

    // The clipboard is written in the click's own call stack by the runtime (`data-rask-copy`): a handler that
    // ran a round trip later would no longer hold the gesture the clipboard asks for. The button then carries
    // `data-copied` for two seconds, which is what shows the tick.
    private static Component CopyButton(string inputId) =>
        ActionButton("Copy to clipboard", ("rask-copy", inputId))
            .Class(UiInputLook.Action)[
            Ui.Icon.Name(Ui.IconName.ClipboardDocumentCheck).Mini.Class(UiInputLook.Copied),
            Ui.Icon.Name(Ui.IconName.ClipboardDocument).Mini.Class(UiInputLook.NotCopied)
        ];

    private Component ViewButton() =>
        ActionButton("Toggle password visibility", _viewing ? ("viewable-open", string.Empty) : null)
            .Class(UiInputLook.Action)
            .OnClick(() => _viewing = !_viewing)[
            Ui.Icon.Name(Ui.IconName.EyeSlash).Mini.Class(_viewing ? null : "hidden"),
            Ui.Icon.Name(Ui.IconName.Eye).Mini.Class(_viewing ? "hidden" : null)
        ];

    // One data bag per button: a second .Data call would replace the first.
    private static HTMLButtonElement ActionButton(string label, params (string Name, string Value)?[] marks)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-button"] = string.Empty };
        foreach (var mark in marks)
        {
            if (mark is (var name, var value))
            {
                data[name] = value;
            }
        }

        return Button.Type(ButtonType.Button).Aria("label", label).Data(data);
    }
}
