using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FileUploadCustomExample : Component
{
    private const string Avatar =
        "relative flex size-20 cursor-pointer items-center justify-center rounded-full border border-zinc-200 "
        + "bg-zinc-100 transition-colors hover:border-zinc-300 hover:bg-zinc-200 dark:border-white/10 "
        + "dark:bg-white/10 dark:hover:bg-white/15 dark:in-data-dragging:bg-white/15";

    private string? _photo;

    protected override Component? Render() =>
        Div[
            Ui.FileUpload.Accept("image/*").OnFiles(Pick).Id("ui-upload-avatar")[
                Div.Class(Avatar)[
                    Span.Class("sr-only")["Upload a profile photo"],
                    Ui.Icon.Name(Ui.IconName.User).Solid.Class("text-zinc-500 dark:text-zinc-400"),
                    Div.Class("absolute right-0 bottom-0 rounded-full bg-white dark:bg-zinc-800")[
                        Ui.Icon.Name(Ui.IconName.ArrowUpCircle).Solid.Class("text-zinc-500 dark:text-zinc-400")
                    ]
                ]
            ],
            Ui.Text.Class("mt-2")[_photo is null ? "No photo chosen." : "Chosen: " + _photo]
        ];

    private void Pick(IReadOnlyList<IRaskFile> files) => _photo = files.Count > 0 ? files[0].Name : null;
}
