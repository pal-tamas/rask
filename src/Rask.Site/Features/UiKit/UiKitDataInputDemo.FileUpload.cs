using Rask.Core.Forms;

namespace Rask.Site.Features.UiKit;

// Flux UI's file upload, example by example, on Rask's own upload: OnFiles hands this component the files, it
// keeps what it shows of them, and draws a Ui.FileItem for each — as Flux's page does from $photos.
public sealed partial class UiKitDataInputDemo
{
    private const string Dropzone = "JPG, PNG, GIF up to 10MB";

    // The classes Flux's own example hands its custom uploader; they are this app's, not the kit's.
    private const string AvatarFrame =
        "relative flex size-20 cursor-pointer items-center justify-center rounded-full border border-zinc-200 "
        + "bg-zinc-100 transition-colors hover:border-zinc-300 hover:bg-zinc-200 dark:border-white/10 "
        + "dark:bg-white/10 dark:hover:bg-white/15 dark:in-data-dragging:bg-white/15";

    // What a picture may weigh to be shown as its own preview, and what the reading example will read.
    private const long PreviewLimit = 2 * 1024 * 1024;
    private const long ReadLimit = 10 * 1024 * 1024;

    private static readonly HashSet<string> Pictures =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    // One file is there from the start, as on Flux's page.
    private readonly List<UploadedFile> _photos = [new("Profile_pic.jpg", 162400, "/img/rask-mark.svg")];
    private readonly List<UploadedFile> _documents = [];
    private readonly List<UploadedFile> _read = [];
    private string? _portrait;

    private Component FileUploadSection() =>
        Section(
            "File upload",
            "A label around a real file input: a click anywhere opens the picker, the input keeps the keyboard, "
            + "and while files are dragged over it the input is laid over the whole area so the drop is the "
            + "browser's own. The files reach the page through OnFiles; the list under each upload is the page's. "
            + "The first reads each picture for its preview; the third reads whatever it is given to the end, "
            + "and its bar is how far that reading has got.",
            Div.Data(Testid("ui-file-upload")).Class("grid max-w-3xl items-start gap-8 sm:grid-cols-2")[
                Div.Key("basic").Data(Testid("ui-upload-basic"))[
                    Ui.FileUpload.Id("demo-photos").Label("Upload files").Multiple().Accept("image/*")
                        .OnFiles(files => KeepWithPreview(_photos, files))[
                        Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text(Dropzone)
                    ],
                    Div.Class("mt-4 flex flex-col gap-2")[
                        _photos.Select((file, index) => Item(file, () => _photos.RemoveAt(index)).Key(file.Name + index))
                    ]
                ],
                Div.Key("inline").Data(Testid("ui-upload-inline"))[
                    Ui.FileUpload.Id("demo-documents").Label("Upload files").Multiple()
                        .OnFiles(files => Keep(_documents, files))[
                        Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text(Dropzone).Inline()
                    ],
                    Div.Class("mt-3 flex flex-col gap-2")[
                        _documents.Select((file, index) => Item(file, () => _documents.RemoveAt(index)).Key(file.Name + index))
                    ]
                ],
                Div.Key("progress").Data(Testid("ui-upload-progress"))[
                    // Flux's page shows this one standing still at 12 %. Here it moves: the runtime marks the upload
                    // while the handler runs and reports how much of the files the handler has read.
                    Ui.FileUpload.Id("demo-progress").Label("Upload files").Multiple()
                        .OnFiles(files => ReadToTheEnd(_read, files))[
                        Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text(Dropzone).WithProgress().Inline()
                    ],
                    Div.Class("mt-3 flex flex-col gap-2")[
                        _read.Select((file, index) => Item(file, () => _read.RemoveAt(index)).Key(file.Name + index))
                    ]
                ],
                Div.Key("disabled").Data(Testid("ui-upload-disabled"))[
                    Ui.FileUpload.Id("demo-disabled").Label("Upload files").Multiple().Disabled()[
                        Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text(Dropzone).Inline()
                    ]
                ],
                Div.Key("custom").Data(Testid("ui-upload-custom"))[
                    Ui.FileUpload.Id("demo-avatar").Accept("image/*")
                        .OnFiles(files => { _portrait = files.Count > 0 ? files[0].Name : null; })[
                        Div.Class(AvatarFrame)[
                            Span.Class("sr-only")["Upload a profile photo"],
                            Ui.Icon.Name(Ui.IconName.User).Solid.Class("text-zinc-500 dark:text-zinc-400"),
                            Div.Class("absolute right-0 bottom-0 rounded-full bg-white dark:bg-zinc-800")[
                                Ui.Icon.Name(Ui.IconName.ArrowUpCircle).Solid.Class("text-zinc-500 dark:text-zinc-400")
                            ]
                        ]
                    ],
                    Ui.Text.Class("mt-2")[_portrait is null ? "No photo chosen." : "Chosen: " + _portrait]
                ]
            ]);

    // The handler is written where the list is, in this component: a handler re-renders the component it
    // closes over, and one that closed over the list alone would remove the file and redraw nothing.
    private static UiFileItem Item(UploadedFile file, Action remove) =>
        Ui.FileItem.Heading(file.Name).Image(file.Image).Size(file.Size)
            .Actions(Ui.FileItemRemove.AriaLabel("Remove file: " + file.Name).OnClick(remove));

    // A file can be read only until its handler returns, so what the list shows is taken here.
    private static void Keep(List<UploadedFile> kept, IReadOnlyList<IRaskFile> files) =>
        kept.AddRange(files.Select(file => new UploadedFile(file.Name, file.Size)));

    private static async Task KeepWithPreview(List<UploadedFile> kept, IReadOnlyList<IRaskFile> files)
    {
        foreach (var file in files)
        {
            kept.Add(new UploadedFile(file.Name, file.Size, await Preview(file)));
        }
    }

    // The picture itself as a data: address. Its type is the browser's word, so only the four an <img> draws
    // without running anything are taken, and only up to a size worth inlining.
    private static async Task<string?> Preview(IRaskFile file)
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

    // Reads every byte and keeps none: the size shown is what arrived, not what the browser said.
    private static async Task ReadToTheEnd(List<UploadedFile> kept, IReadOnlyList<IRaskFile> files)
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

            kept.Add(new UploadedFile(file.Name, arrived));
        }
    }
}
