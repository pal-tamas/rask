namespace Rask;

/// <summary>
///     Flux's <c>flux:file-item</c>: one file as a row — a preview or an icon, its name, its size, and whatever
///     the page puts in <see cref="Actions" />.
/// </summary>
/// <remarks>
///     The page draws these from its own state, one per file it kept from <see cref="UiFileUpload.OnFiles" />.
/// </remarks>
public sealed partial class UiFileItem : Component
{
    private const string Frame =
        "flex min-h-10 cursor-default items-start overflow-hidden rounded-lg border border-zinc-200 "
        + "border-b-zinc-300/80 bg-white text-sm shadow-xs dark:border-white/10 dark:bg-white/10";

    // No example on Flux's page draws an invalid item, so this is its invalid input's border, not a measurement.
    private const string InvalidFrame =
        "flex min-h-10 cursor-default items-start overflow-hidden rounded-lg border border-red-500 "
        + "bg-white text-sm shadow-xs dark:border-red-400 dark:bg-white/10";

    private const string Preview =
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

    /// <inheritdoc />
    protected override Component? Render()
    {
        var detail = Text ?? (Size is { } bytes ? UiFileSize.Format(bytes) : null);

        return Div.Class(UiClass.Compose(Invalid == true ? InvalidFrame : Frame, Class)).Data(Marker.With(null))[
            Lead(detail is not null),
            Div.Class("me-3 flex grow flex-col justify-center gap-1 overflow-hidden py-[9px]").Data(ContentSlot.With(null))[
                Div.Class("truncate font-medium text-zinc-500 dark:text-white/80")[Heading],
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
        if (Image is null)
        {
            return twoLines
                ? Div.Class("flex items-baseline p-[7px]")[icon.Solid.Class("text-zinc-400")]
                : Div.Class("flex items-baseline p-[11px]")[icon.Micro.Class("text-zinc-400")];
        }

        return Div.Class("flex items-baseline p-[7px]")[
            icon.Solid.Class("hidden text-zinc-400"),
            Div.Class(Preview).Data(ImageSlot.With(null))[Img.Src(Image).Alt("").Class("size-full max-w-full object-cover")]
        ];
    }
}
