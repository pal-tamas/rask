using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadMultipleExample : Component
{
    private readonly List<(string Name, long Size)> _photos = [];
    private string _saved = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.FileUpload.Label("Upload files").Multiple().Accept("image/*").OnFiles(Keep).Id("ui-upload-multiple")[
                Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("JPG, PNG, GIF up to 10MB")
            ],
            Div.Class("flex flex-col gap-2")[
                _photos.Select((photo, index) =>
                    Ui.FileItem.Key(photo.Name + index).Heading(photo.Name).Size(photo.Size)
                        .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + photo.Name).OnClick(() => _photos.RemoveAt(index))))
            ],
            Ui.Button.OnClick(Save)["Save"],
            Ui.Text[_saved]
        ];

    private void Keep(IReadOnlyList<IRaskFile> files) => _photos.AddRange(files.Select(file => (file.Name, file.Size)));

    private void Save() => _saved = _photos.Count == 1 ? "Saved 1 file." : $"Saved {_photos.Count} files.";
}
