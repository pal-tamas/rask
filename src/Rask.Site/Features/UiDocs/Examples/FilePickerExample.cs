using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FilePickerExample : Component
{
    private readonly Registration _registration = new();
    private string _saved = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_registration).OnSubmit(Save).Id("ui-file-picker-form").Class("max-w-md space-y-6")[
                Ui.FilePicker.Bind(() => _registration.Scan).Label("Registration scan").MaxSize(100_000)
                    .Heading("Drop the scan here or click to browse").Text("PDF or image, up to 100 kB").Id("ui-file-picker"),
                Ui.Button.Primary.Submit["Save"]
            ],
            Ui.Text.Id("ui-file-picker-saved")[_saved]
        ];

    // In a server app this model is an operation's form, and the picked file is stored with the row as an Attachment.
    private void Save(Registration registration) =>
        _saved = registration.Scan is { } scan ? "Saved " + scan.Name + " (" + scan.Size + " bytes)." : "No file to save.";

    public sealed class Registration
    {
        public PickedFile? Scan { get; set; }
    }
}
