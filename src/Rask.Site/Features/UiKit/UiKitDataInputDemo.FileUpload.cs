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

    // One file is there from the start, as on Flux's page: a preview is an address, and a file that has only
    // just been chosen has none until it is stored somewhere.
    private readonly List<UploadedFile> _photos = [new("Profile_pic.jpg", 162400, "/img/rask-mark.svg")];
    private readonly List<UploadedFile> _documents = [];
    private string? _portrait;

    private Component FileUploadSection() =>
        Section(
            "File upload",
            "A label around a real file input: a click anywhere opens the picker, the input keeps the keyboard, "
            + "and while files are dragged over it the input is laid over the whole area so the drop is the "
            + "browser's own. The files reach the page through OnFiles; the list under each upload is the page's.",
            Div.Data(Testid("ui-file-upload")).Class("grid max-w-3xl items-start gap-8 sm:grid-cols-2")[
                Div.Key("basic").Data(Testid("ui-upload-basic"))[
                    Ui.FileUpload.Id("demo-photos").Label("Upload files").Multiple().Accept("image/*")
                        .OnFiles(files => Keep(_photos, files))[
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
                    // Flux's page shows this one standing still at 12 %. Here the runtime marks the upload while
                    // its handler runs, and the bar is how much of the files that handler has read.
                    Ui.FileUpload.Id("demo-progress").Label("Upload files").Multiple()
                        .OnFiles(files => Keep(_documents, files))[
                        Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text(Dropzone).WithProgress().Inline()
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
}
