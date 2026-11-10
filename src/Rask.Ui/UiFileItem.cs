using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:file-item</c>: one file as a row — a preview or an icon, its name, its size, and whatever
///     the page puts in <see cref="Actions" />.
/// </summary>
/// <remarks>
///     The page draws these from its own state, one per file it kept from <see cref="UiFileUpload.OnFiles" />.
///     <see cref="Preview" /> shows a chosen image before it is saved anywhere, drawn by the browser from the file
///     itself.
/// </remarks>
public sealed partial class UiFileItem : Component
{
    private const string Frame =
        "[:where(&)]:flex min-h-10 cursor-default items-start overflow-hidden [:where(&)]:rounded-lg border border-zinc-200 "
        + "border-b-zinc-300/80 bg-white [:where(&)]:text-base sm:text-sm shadow-xs dark:border-white/10 dark:bg-white/10";

    // No example on Flux's page draws an invalid item, so this is its invalid input's border, not a measurement.
    private const string InvalidFrame =
        "[:where(&)]:flex min-h-10 cursor-default items-start overflow-hidden [:where(&)]:rounded-lg border border-red-500 "
        + "bg-white [:where(&)]:text-base sm:text-sm shadow-xs dark:border-red-400 dark:bg-white/10";

    private const string PreviewFrame =
        "relative me-1 size-11 overflow-hidden rounded-sm after:absolute after:inset-0 after:rounded-sm "
        + "after:inset-ring after:inset-ring-black/7 dark:after:inset-ring-white/10";

    private static readonly UiPartMarker Marker = new("ui-file-item");

    private static readonly UiPartMarker ImageSlot = new("slot", "image");

    private static readonly UiPartMarker ContentSlot = new("slot", "content");

    private static readonly UiPartMarker ActionsSlot = new("slot", "actions");

    /// <summary>The file's name, or another title for it.</summary>
    public string? Heading { get; set; }

    /// <summary>The line under the heading. Written from <see cref="Size" /> when unset.</summary>
    public string? Text { get; set; }

    /// <summary>The address of a preview image, shown in place of the icon.</summary>
    public string? Image { get; set; }

    /// <summary>
    ///     A file the reader chose, shown in place of the icon when it is an image: <c>.Preview(photo)</c>, with the
    ///     <c>IRaskFile</c> the upload's handler received. The browser draws it from the file it still holds — no
    ///     byte is sent or stored for it, and it shows at once. A file that is no image, or one this tab did not see
    ///     chosen (after a reload), keeps the icon; give a saved file's address to <see cref="Image" /> instead.
    /// </summary>
    public IRaskFile? Preview { get; set; }

    /// <summary>The file's size in bytes, shown as B, KB, MB or GB.</summary>
    public long? Size { get; set; }

    /// <summary>The icon shown when there is no image. A document when unset.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Draws the item as one that failed.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Flux's <c>actions</c> slot: what sits at the end of the row — a <see cref="UiFileItemRemove" />.</summary>
    public Component? Actions { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    // What names the chosen file to the browser: its size, its time and its name, the facts both hosts hand on.
    private string? Previewed() =>
        Preview is { } file && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{file.Size}:{file.LastModified.ToUnixTimeMilliseconds()}:{file.Name}")
            : null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var detail = Text ?? (Size is { } bytes ? UiFileSize.Format(bytes) : null);

        return Div.Class(Invalid == true ? InvalidFrame : Frame, Class).Data(Marker.With(null))[
            Lead(detail is not null),
            Div.Class("me-3 flex grow flex-col justify-center gap-1 overflow-hidden py-[9px]").Data(ContentSlot.With(null))[
                Div.Class("truncate text-sm font-medium text-zinc-500 dark:text-white/80")[Heading],
                detail is null ? null : Div.Class("text-xs text-zinc-500")[detail]
            ],
            Actions is null
                ? null
                : Div.Class("flex shrink-0 items-center gap-2 p-[3px]").Data(ActionsSlot.With(null))[Actions]
        ];
    }

    // A row of one line takes the small icon; a row of two, or one with a preview, the large one. Flux leaves
    // the icon in the markup under a preview, not displayed.
    private Component Lead(bool twoLines)
    {
        var icon = Ui.Icon.Name(Icon ?? Ui.IconName.Document);
        if (Image is null && Previewed() is { } chosen)
        {
            // The browser fills the source from the file (rask-preview.ts); until it does, or if it never can, the
            // icon shows through, so a file this tab never saw is drawn as one with no preview.
            return Div.Class("group/preview flex items-baseline p-[7px]")[
                icon.Solid.Class("text-zinc-400 group-has-[img[src]]/preview:hidden"),
                Div.Class(PreviewFrame, "not-has-[img[src]]:hidden").Data(ImageSlot.With(null))[
                    Img.Alt("").Class("size-full max-w-full object-cover").Data("rask-preview", chosen)
                ]
            ];
        }

        if (Image is null)
        {
            return twoLines
                ? Div.Class("flex items-baseline p-[7px]")[icon.Solid.Class("text-zinc-400")]
                : Div.Class("flex items-baseline p-[11px]")[icon.Micro.Class("text-zinc-400")];
        }

        return Div.Class("flex items-baseline p-[7px]")[
            icon.Solid.Class("hidden text-zinc-400"),
            Div.Class(PreviewFrame).Data(ImageSlot.With(null))[Img.Src(Image).Alt("").Class("size-full max-w-full object-cover")]
        ];
    }
}
