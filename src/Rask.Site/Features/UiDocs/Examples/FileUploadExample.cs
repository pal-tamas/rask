using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadExample : Component
{
    // What a picture may weigh to be shown as its own preview.
    private const long PreviewLimit = 2 * 1024 * 1024;

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    // One file is there from the start, as on Flux's page.
    private readonly List<Photo> _photos = [new("Profile_pic.jpg", 162400, "/img/avatars/sky.svg")];

    protected override Component? Render() =>
        Div[
            Ui.FileUpload.Label("Upload files").Multiple().Accept("image/*").OnFiles(Keep).Id("ui-upload-photos")[
                Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("JPG, PNG, GIF up to 10MB")
            ],
            Div.Class("mt-4 flex flex-col gap-2")[
                _photos.Select((photo, index) =>
                    Ui.FileItem.Key(photo.Name + index).Heading(photo.Name).Image(photo.Preview).Size(photo.Size)
                        .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + photo.Name).OnClick(() => _photos.RemoveAt(index))))
            ]
        ];

    // A file can be read only while its handler runs, so what the list shows is taken here.
    private async Task Keep(IReadOnlyList<IRaskFile> files)
    {
        foreach (var file in files)
        {
            _photos.Add(new Photo(file.Name, file.Size, await ReadPreview(file)));
        }
    }

    // The picture as a data: address. Its type is the browser's word, so only the four an <img> draws are taken.
    private static async Task<string?> ReadPreview(IRaskFile file)
    {
        if (file.Size > PreviewLimit || !Pictures.Contains(file.ContentType))
        {
            return null;
        }

        await using var stream = file.OpenReadStream(PreviewLimit);
        using var bytes = new MemoryStream((int)file.Size);
        await stream.CopyToAsync(bytes);

        return $"data:{file.ContentType};base64,{Convert.ToBase64String(bytes.GetBuffer(), 0, (int)bytes.Length)}";
    }

    private sealed record Photo(string Name, long Size, string? Preview);
}
