using Rask.Core;

namespace Rask;

// What stands on the page: an input with a swatch button, or one button showing the colour.
public sealed partial class UiColorPicker
{
    private Component InputTrigger(UiWithField field)
    {
        var aria = OpenerAria(field.LabelledBy, RaskStrings.Get(RaskString.ColorPickerInput, "Color"));
        aria["autocomplete"] = "none";
        foreach (var (key, value) in field.Aria)
        {
            aria[key] = value;
        }

        var actions = (Clearable == true || Copyable == true) && !Off;

        return Div.Class(UiColorPickerLook.Trigger, UiColorPickerLook.TriggerSize(Height), actions ? UiColorPickerLook.TriggerWithAction : null)
            // While a message shows under the field, the first key of a correction takes it away.
            .Attributes([.. Marked("data-ui-color-picker-trigger", field.Invalid), .. UiFormCommit.FirstEdit(_bound.Accessor, _bound.Context)])[
            Button
                .Type(ButtonType.Button)
                .Class(UiColorPickerLook.SwatchButton, UiColorPickerLook.SwatchSize(Height))
                .Disabled(Off)
                .Aria(OpenerAria(null, OpenerLabel()))
                .Attributes(("data-ui-color-picker-swatch-button", null), ("command", "show-modal"), ("commandfor", DialogId), UiColorPickerMarks.Named("opener"))[
                Preview()
            ],
            Input
                .Value(_refused ?? _kept.Text(Written))
                .Type(InputType.Text)
                .Id(field.ControlId)
                .Placeholder(Hint)
                .Role("combobox")
                .Disabled(Off)
                .Class(UiColorPickerLook.TriggerInput)
                .Aria(aria)
                .Attributes(("size", "1"), ("data-ui-color-picker-input", null), UiColorPickerMarks.Named("text")),
            Clearable == true && !Off ? ClearButton() : null,
            Copyable == true && !Off ? CopyButton(field.ControlId) : null
        ];
    }

    private Component ButtonTrigger(UiWithField field)
    {
        var aria = OpenerAria(field.LabelledBy, null);
        foreach (var (key, value) in field.Aria)
        {
            aria[key] = value;
        }

        var empty = _kept.Empty;

        return Button
            .Type(ButtonType.Button)
            .Id(field.ControlId)
            .Class(UiColorPickerLook.ButtonTrigger, UiColorPickerLook.ButtonSize(Height))
            .Disabled(Off)
            .Aria(aria)
            .Attributes([.. Marked("data-ui-color-picker-button", field.Invalid), ("command", "show-modal"), ("commandfor", DialogId), UiColorPickerMarks.Named("button")])[
            Div.Class(UiColorPickerLook.SwatchBox, UiColorPickerLook.SwatchSize(Height))[Preview()],
            Span.Class(UiColorPickerLook.Display).Data("ui-color-picker-display", string.Empty)[
                Span.Class(UiColorPickerLook.DisplayPlaceholder).Hidden(!empty)
                    .Attributes(("data-ui-color-picker-placeholder", null), UiColorPickerMarks.Named("placeholder"))[Hint],
                // Never without a text the browser can rewrite: hidden, it says the placeholder.
                Span.Hidden(empty).Attributes(("data-ui-color-picker-value", null), UiColorPickerMarks.Named("value"))[empty ? Hint : _kept.Text(Written)]
            ],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiColorPickerLook.Chevron)
        ];
    }

    private HTMLDivElement Preview() =>
        Div.Class(UiColorPickerLook.Preview)
            .Style("background-color:" + _kept.Css)
            .Attributes(("data-ui-color-picker-preview", null), UiColorPickerMarks.Named("preview"));

    // Flux's clear button is its small subtle button drawn as a <div>, out of the tab order. Always there and
    // out of sight while there is no colour, so the button after it keeps its place in the tree.
    // The runtime empties the picker on a press.
    private Component ClearButton() =>
        Div.Class(UiColorPickerLook.Clear)
            .Hidden(_kept.Empty && _refused is null)
            .TabIndex(-1)
            .Aria("label", RaskStrings.Get(RaskString.ColorPickerClear, "Clear color"))
            .Attributes(("data-ui-button", null), UiColorPickerMarks.Named("clear"))[
            Ui.Icon.Name(Ui.IconName.XMark).Micro
        ];

    // The clipboard is written in the click's own call stack by the runtime (`data-rask-copy`): the input's
    // text, which is the colour as the picker writes it. The button says `data-copied` for two seconds.
    private static Component CopyButton(string inputId) =>
        Div.Class(UiColorPickerLook.Clear)
            .TabIndex(-1)
            .Aria("label", RaskStrings.Get(RaskString.InputCopy, "Copy to clipboard"))
            .Attributes(("data-ui-button", null), ("data-ui-color-picker-copy", null), ("data-rask-copy", inputId))[
            Ui.Icon.Name(Ui.IconName.ClipboardDocumentCheck).Mini.Class(UiInputLook.Copied),
            Ui.Icon.Name(Ui.IconName.ClipboardDocument).Mini.Class(UiInputLook.NotCopied)
        ];

    // "Open color picker, Red": the palette's name for the colour when it has one, the value otherwise.
    private string OpenerLabel() =>
        _kept.Empty
            ? RaskStrings.Get(RaskString.ColorPickerOpen, "Open color picker")
            : RaskStrings.Get(RaskString.ColorPickerOpenWith, "Open color picker, {0}", Palette.Picked(Matched(_kept))?.Named ?? _kept.Text(Written));

    private Dictionary<string, string?> OpenerAria(string? labelled, string? label)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (label is not null)
        {
            aria["label"] = label;
        }

        aria["haspopup"] = "dialog";
        aria["controls"] = DialogId;
        // The browser's while the dialog shows (rask-toggle.ts, rask-color.ts).
        aria["expanded"] = "false";
        if (labelled is not null)
        {
            aria["labelledby"] = labelled;
        }

        return aria;
    }

    private static (string Name, string? Value)[] Marked(string marker, bool invalid) =>
        invalid
            ? [("data-ui-group-target", null), (marker, null), ("data-invalid", null)]
            : [("data-ui-group-target", null), (marker, null)];
}
