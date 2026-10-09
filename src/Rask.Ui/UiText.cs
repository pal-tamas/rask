namespace Rask;

/// <summary>
/// Body copy, in the kit's type scale and inks.
/// </summary>
/// <remarks>
/// Flux UI's <c>flux:text</c>. A paragraph by default; <see cref="Inline" /> makes it a <c>&lt;span&gt;</c> for a
/// run inside another line. <see cref="Variant" /> is how much it stands out and <see cref="Color" /> paints it
/// outright; a colour wins over a variant.
/// </remarks>
public sealed partial class UiText : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-text");

    /// <summary>How big it looks. Unset, <see cref="Ui.TextSize.Default" />.</summary>
    public Ui.TextSize? Size { get; set; }

    /// <summary>How much it stands out. Unset, <see cref="Ui.TextVariant.Default" />.</summary>
    public Ui.TextVariant? Variant { get; set; }

    /// <summary>
    ///     Paints it one of Tailwind's hues, and writes <c>data-color</c>. Unset — or one of the five neutrals,
    ///     which Flux's text does not take — the variant's ink.
    /// </summary>
    public Ui.Color? Color { get; set; }

    /// <summary>A <c>&lt;span&gt;</c> rather than a paragraph.</summary>
    public bool? Inline { get; set; }

    /// <inheritdoc />
    protected override string TagName => Inline == true ? "span" : "p";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            SizeClass(Size),
            Color is { } color && ColorClass(color) is { Length: > 0 } hue ? hue : VariantClass(Variant),
            Weight,
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() =>
        Color is { } color && ColorName(color) is { Length: > 0 } name
            ? Marker.With(Data, "color", name)
            : Marker.With(Data);

    // Text is regular weight wherever it stands — inline in a heading, in a callout's heading — and yields to
    // a weight the call site hands it, which is why the rule carries no specificity (measured on Flux's
    // timeline page: 400 inside a 500 heading, 500 with `font-medium`).
    private const string Weight = "[:where(&)]:font-normal";

    private static string SizeClass(Ui.TextSize? size) => size switch
    {
        Ui.TextSize.Sm => "text-xs",
        Ui.TextSize.Lg => "text-base",
        Ui.TextSize.Xl => "text-lg",
        _ => "text-sm",
    };

    private static string VariantClass(Ui.TextVariant? variant) => variant switch
    {
        Ui.TextVariant.Strong => "text-zinc-800 dark:text-white",
        Ui.TextVariant.Subtle => "text-zinc-400 dark:text-white/50",
        _ => "text-zinc-500 dark:text-white/70",
    };

    // Flux's text takes the seventeen chromatic hues. Slate, Gray, Zinc, Neutral and Stone fall through to
    // "" here and in ColorName: a neutral is the default styling, which is the variant's own zinc.
    private static string ColorClass(Ui.Color color) => color switch
    {
        Ui.Color.Red => "text-red-600 dark:text-red-400",
        Ui.Color.Orange => "text-orange-600 dark:text-orange-400",
        Ui.Color.Amber => "text-amber-600 dark:text-amber-400",
        Ui.Color.Yellow => "text-yellow-600 dark:text-yellow-400",
        Ui.Color.Lime => "text-lime-600 dark:text-lime-400",
        Ui.Color.Green => "text-green-600 dark:text-green-400",
        Ui.Color.Emerald => "text-emerald-600 dark:text-emerald-400",
        Ui.Color.Teal => "text-teal-600 dark:text-teal-400",
        Ui.Color.Cyan => "text-cyan-600 dark:text-cyan-400",
        Ui.Color.Sky => "text-sky-600 dark:text-sky-400",
        Ui.Color.Blue => "text-blue-600 dark:text-blue-400",
        Ui.Color.Indigo => "text-indigo-600 dark:text-indigo-400",
        Ui.Color.Violet => "text-violet-600 dark:text-violet-400",
        Ui.Color.Purple => "text-purple-600 dark:text-purple-400",
        Ui.Color.Fuchsia => "text-fuchsia-600 dark:text-fuchsia-400",
        Ui.Color.Pink => "text-pink-600 dark:text-pink-400",
        Ui.Color.Rose => "text-rose-600 dark:text-rose-400",
        _ => "",
    };

    private static string ColorName(Ui.Color color) => color switch
    {
        Ui.Color.Red => "red",
        Ui.Color.Orange => "orange",
        Ui.Color.Amber => "amber",
        Ui.Color.Yellow => "yellow",
        Ui.Color.Lime => "lime",
        Ui.Color.Green => "green",
        Ui.Color.Emerald => "emerald",
        Ui.Color.Teal => "teal",
        Ui.Color.Cyan => "cyan",
        Ui.Color.Sky => "sky",
        Ui.Color.Blue => "blue",
        Ui.Color.Indigo => "indigo",
        Ui.Color.Violet => "violet",
        Ui.Color.Purple => "purple",
        Ui.Color.Fuchsia => "fuchsia",
        Ui.Color.Pink => "pink",
        Ui.Color.Rose => "rose",
        _ => "",
    };
}
