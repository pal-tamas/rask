using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadInlineExample : Component
{
    private readonly List<string> _files = ["Profile_pic.jpg", "Brand_banner.jpg"];

    protected override Component? Render() =>
        Div[
            Ui.FileUpload.Label("Upload files").Multiple().OnFiles(Keep).Id("ui-upload-inline")[
                Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("JPG, PNG, GIF up to 10MB").Inline()
            ],
            Div.Class("mt-3 flex flex-col gap-2")[
                _files.Select((name, index) =>
                    Ui.FileItem.Key(name + index).Heading(name)
                        .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + name).OnClick(() => _files.RemoveAt(index))))
            ]
        ];

    private void Keep(IReadOnlyList<IRaskFile> files) => _files.AddRange(files.Select(file => file.Name));
}
