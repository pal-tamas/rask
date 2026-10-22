using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadSingleExample : Component
{
    private (string Name, long Size)? _photo;
    private string _saved = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.FileUpload.Label("Upload file").Accept("image/*").OnFiles(Pick).Id("ui-upload-single")[
                Ui.FileUploadDropzone.Heading("Drop file here or click to browse").Text("JPG, PNG, GIF up to 10MB")
            ],
            Div.Class("flex flex-col gap-2")[
                _photo is { } photo
                    ? Ui.FileItem.Heading(photo.Name).Size(photo.Size)
                        .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + photo.Name).OnClick(() => { _photo = null; }))
                    : null
            ],
            Ui.Button.OnClick(Save)["Save"],
            Ui.Text[_saved]
        ];

    // Where Livewire stores the file on save, a Rask page reads it in OnFiles: this one keeps its name and size.
    private void Pick(IReadOnlyList<IRaskFile> files) => _photo = files.Count > 0 ? (files[0].Name, files[0].Size) : null;

    private void Save() => _saved = _photo is { } photo ? "Saved " + photo.Name + "." : "No file to save.";
}
