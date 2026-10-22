using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadProgressExample : Component
{
    private const long ReadLimit = 10 * 1024 * 1024;

    private readonly List<(string Name, long Size)> _read = [];

    protected override Component? Render() =>
        Div[
            Ui.FileUpload.Label("Upload files").Multiple().OnFiles(ReadToTheEnd).Id("ui-upload-progress")[
                Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("JPG, PNG, GIF up to 10MB").WithProgress().Inline()
            ],
            Div.Class("mt-3 flex flex-col gap-2")[
                _read.Select((file, index) => Ui.FileItem.Key(file.Name + index).Heading(file.Name).Size(file.Size))
            ]
        ];

    // The bar is how much of the files this handler has read: here, every byte, to the end.
    private async Task ReadToTheEnd(IReadOnlyList<IRaskFile> files)
    {
        var buffer = new byte[64 * 1024];
        foreach (var file in files.Where(file => file.Size <= ReadLimit))
        {
            long arrived = 0;
            await using var stream = file.OpenReadStream(ReadLimit);
            for (var read = await stream.ReadAsync(buffer); read > 0; read = await stream.ReadAsync(buffer))
            {
                arrived += read;
            }

            _read.Add((file.Name, arrived));
        }
    }
}
