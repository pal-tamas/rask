namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.FileUpload.Label("Upload files").Multiple().Disabled().Id("ui-upload-disabled")[
            Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("JPG, PNG, GIF up to 10MB").Inline()
        ];
}
