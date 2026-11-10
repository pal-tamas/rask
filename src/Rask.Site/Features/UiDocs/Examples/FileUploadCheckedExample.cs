using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadCheckedExample : Component
{
    private readonly List<IRaskFile> _photos = [];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.FileUpload.Label("Photos").Multiple().Accept("image/*").MaxSize(2.Megabytes).OnFiles(Keep).Id("ui-upload-checked")[
                Ui.FileUploadDropzone.Heading("Drop photos here or click to browse").Text("Pictures up to 2 MB").WithProgress().Inline()
            ],
            Div.Class("flex flex-col gap-2")[
                _photos.Select((photo, index) =>
                    Ui.FileItem.Key(photo.Name + index).Heading(photo.Name).Size(photo.Size).Preview(photo)
                        .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + photo.Name).OnClick(() => _photos.RemoveAt(index))))
            ]
        ];

    // Only what passed the checks arrives here: a file too large or of another kind never left the browser.
    private void Keep(IReadOnlyList<IRaskFile> files) => _photos.AddRange(files);
}
