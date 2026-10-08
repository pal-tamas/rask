using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/file-upload</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The words and props are the rendered page's: its first example takes one file where the markdown says
///     <c>multiple</c>, and its progress example stands still at 12 % — <c>data-loading</c> and the two
///     variables are written on the dropzone there, so they are here.
///     </para>
///     <para>
///     "Livewire integration" has no rendered example: it is two listings of a Livewire component.
///     </para>
/// </remarks>
public sealed partial class FileUploadParity : FluxParity
{
    // The column Flux's docs page centres every example in.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:0 auto";

    // What an app's own Tailwind build emits for the classes the custom uploader hands its own markup.
    private const string Avatar =
        "<style>.parity-avatar{position:relative;display:flex;align-items:center;justify-content:center;width:80px;height:80px;"
        + "border-radius:calc(infinity * 1px);cursor:pointer;border:1px solid oklch(92% .004 286.32);"
        + "background-color:oklch(96.7% .001 286.375);transition-property:color,background-color,border-color,"
        + "outline-color,text-decoration-color,fill,stroke,--tw-gradient-from,--tw-gradient-via,--tw-gradient-to;"
        + "transition-duration:.15s;transition-timing-function:cubic-bezier(.4,0,.2,1)}"
        + ".parity-avatar:hover{border-color:oklch(87.1% .006 286.286);background-color:oklch(92% .004 286.32)}"
        + ".dark .parity-avatar{border-color:color-mix(in oklab,#fff 10%,transparent);background-color:color-mix(in oklab,#fff 10%,transparent)}"
        + ".dark .parity-avatar:hover{background-color:color-mix(in oklab,#fff 15%,transparent)}"
        + ".parity-avatar-icon{color:oklch(55.2% .016 285.938)}.dark .parity-avatar-icon{color:oklch(70.5% .015 286.067)}"
        + ".parity-avatar-corner{position:absolute;bottom:0;right:0;background-color:#fff;border-radius:calc(infinity * 1px)}"
        + ".dark .parity-avatar-corner{background-color:oklch(27.4% .006 286.033)}</style>";

    public override string Page => "file-upload";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style(Column)[
            Div.Style("min-width:320px")[
                Ui.FileUpload.Label("Upload files")[
                    Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("JPG, PNG, GIF up to 10MB")
                ],
                Div.Style("margin-top:16px;display:flex;flex-direction:column;gap:8px")[
                    Ui.FileItem
                        .Heading("Profile_pic.jpg")
                        .Image("https://fluxui.dev/img/demo/user.png")
                        .Size(162400)
                        .Actions(Ui.FileItemRemove)
                ]
            ]
        ]);

        yield return ("inline-layout", Div.Style(Column)[
            Div[
                Ui.FileUpload.Multiple().Label("Upload files")[
                    Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("JPG, PNG, GIF up to 10MB").Inline()
                ],
                Div.Style("margin-top:12px;display:flex;flex-direction:column;gap:8px")[
                    Ui.FileItem.Heading("Profile_pic.jpg").Actions(Ui.FileItemRemove),
                    Ui.FileItem.Heading("Brand_banner.jpg").Actions(Ui.FileItemRemove)
                ]
            ]
        ]);

        // Flux's page shows the bar by writing the uploading state on the dropzone, at a standstill.
        yield return ("progress-indicator", Div.Style(Column)[
            Div[
                Ui.FileUpload.Multiple().Label("Upload files")[
                    Ui.FileUploadDropzone
                        .Heading("Drop files or click to browse")
                        .Text("JPG, PNG, GIF up to 10MB")
                        .WithProgress()
                        .Inline()
                        .Data("loading", "")
                        .Style("--ui-file-upload-progress:12%;--ui-file-upload-progress-as-string:'12%'")
                ]
            ]
        ]);

        yield return ("disabled-state", Div.Style(Column)[
            Div[
                Ui.FileUpload.Multiple().Label("Upload files").Disabled()[
                    Ui.FileUploadDropzone.Heading("Drop files or click to browse").Text("JPG, PNG, GIF up to 10MB").Inline()
                ]
            ]
        ]);

        // wire:model and $photo are Livewire's: the avatar is shown with no file chosen, as on Flux's page.
        yield return ("custom-uploader", Div.Style(Column)[
            Raw.Value(Avatar),
            Div[
                Ui.FileUpload[
                    Div.Class("parity-avatar")[
                        Ui.Icon.Name(Ui.IconName.User).Solid.Class("parity-avatar-icon"),
                        Div.Class("parity-avatar-corner")[
                            Ui.Icon.Name(Ui.IconName.ArrowUpCircle).Solid.Class("parity-avatar-icon")
                        ]
                    ]
                ]
            ]
        ]);
    }
}
