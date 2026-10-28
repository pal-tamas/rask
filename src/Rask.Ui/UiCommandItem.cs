namespace Rask;

/// <summary>Flux UI's <c>flux:command.item</c>: one command of a <see cref="UiCommandItems" /> list.</summary>
/// <remarks>
///     Its children are its words, and what the search is matched against. <see cref="OnClick" /> is what pressing it
///     does — a click, or Enter while it is lit — and Flux's <c>wire:click</c>. The lit command is the browser's:
///     <c>data-active</c>, written by the runtime as the pointer and the arrows move.
/// </remarks>
public sealed partial class UiCommandItem : Component
{
    private static readonly UiPartMarker Marker = new("ui-command-item");

    private const string Look =
        "flex h-10 cursor-default items-center rounded-md px-2 py-1.5 text-sm font-medium text-zinc-800 outline-none "
        + "data-active:bg-zinc-100 data-hidden:hidden dark:text-white dark:data-active:bg-zinc-600";

    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>The icon at the start of the row.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of the icon. The 24px <see cref="Ui.IconVariant.Outline" /> unless this says otherwise.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>A keyboard shortcut shown at the end of the row — <c>"⌘K"</c>. A hint: the page binds the key.</summary>
    public string? Kbd { get; set; }

    /// <summary>What pressing the command does.</summary>
    public Callback OnClick { get; set; }

    /// <summary>Classes for the row.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var row = Div.Id("ui-command-item-" + _instance.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Class(Look, Class)
            .Data(Marker.With(null))
            .Role("option")
            .Attributes(("data-rask-match", UiMenuRow.Label(Children)));
        if (OnClick.HasValue)
        {
            row = row.OnClick(OnClick);
        }

        Component?[] content =
        [
            Icon is { } icon
                ? Div.Class("relative")[Ui.Icon.Name(icon).Variant(IconVariant ?? Ui.IconVariant.Outline).Class("me-2 text-zinc-400 in-data-active:text-current")]
                : null,
            .. Children ?? [],
            Kbd is { } kbd
                ? Div.Class("ms-auto inline-flex rounded-sm bg-zinc-800/5 px-1 py-0.5 dark:bg-white/10")[
                    Span.Class("text-xs font-medium text-zinc-500 dark:text-zinc-300")[kbd]
                ]
                : null
        ];

        return row[content];
    }
}
