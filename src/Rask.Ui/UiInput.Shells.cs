using System.Globalization;
using Rask.Core.Forms;

namespace Rask;

// The two forms that are not a text box: the input drawn as a button, and the file input.
public sealed partial class UiInput<T>
{
    private const string NoFile = "No file chosen";

    private string? _chosen;

    // Flux marks this one with nothing — no data-flux-input — so neither does this.
    private Component AsButton(UiWithField field) =>
        Button
            .Type(ButtonType.Button)
            .Id(field.ControlId)
            .Disabled(Disabled == true)
            .Aria(field.Aria)
            .OnClick(OnClick)
            .Class(UiClass.Compose(UiInputLook.AsButton, UiInputLook.Padding(Icon is not null, Kbd is not null), Class))[
            Icon is { } icon ? Div.Class(UiInputLook.AsButtonLeading)[Glyph(icon)] : null,
            Div.Class(UiInputLook.AsButtonText)[Placeholder],
            Kbd is { } kbd ? Div.Class(UiInputLook.AsButtonKbd)[kbd] : null
        ];

    // A <label> around the real input, which is out of sight: a click anywhere on it opens the picker, and
    // the input keeps the keyboard (Enter and Space open it too) — what Flux's div does with script.
    // RaskMarkup.Label, qualified: this type's Label property hides the chain entry of the same name.
    private Component FileShell(UiWithField field) =>
        RaskMarkup.Label.Class(UiClass.Compose(UiInputLook.File, Class)).Data("ui-input-file", "")[
            Input
                .Of<string>()
                .Id(field.ControlId)
                .Name(Name)
                .Type(InputType.File)
                .Multiple(Multiple == true)
                .Disabled(Disabled == true)
                .OnFiles(Chosen)
                .Aria(field.Aria)
                .Attributes(UiInputLook.FileMarks)
                .Class("sr-only"),
            Div.Class(UiInputLook.FileButton).Data("ui-button", "").Aria("hidden", "true")[
                Multiple == true ? "Choose files" : "Choose file"
            ],
            Div.Class(UiInputLook.FileName).Aria("hidden", "true")[_chosen ?? NoFile]
        ];

    private async Task Chosen(IReadOnlyList<IRaskFile> files)
    {
        _chosen = files.Count switch
        {
            0 => null,
            1 => files[0].Name,
            _ => string.Create(CultureInfo.InvariantCulture, $"{files.Count} files"),
        };

        await OnFiles.Invoke(files).ConfigureAwait(false);
    }
}
