namespace Rask;

/// <summary>
/// A heading: how big it looks, and what level it is, decided separately.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>flux:heading</c>. <see cref="Size" /> is the visual scale; <see cref="Level" /> is the outline
/// a screen reader navigates by. Two properties because the two go wrong in opposite directions: a card
/// title that must look small is still the second level of the page, and a big number on a dashboard is
/// not a heading at all.
/// </para>
/// <para>
/// Without a <see cref="Level" /> it is a <c>&lt;div&gt;</c> — a title that does not belong in the outline.
/// The line under it is a <see cref="UiText" />: <c>Ui.Heading["Orders"]</c>, then <c>Ui.Text["…"]</c>.
/// </para>
/// </remarks>
public sealed partial class UiHeading : UiElement
{
    private static readonly IReadOnlyDictionary<string, string?> Marker = UiDataMarker.Of("ui-heading");

    /// <summary>How big it looks. Unset, <see cref="Ui.HeadingSize.Base" />.</summary>
    public Ui.HeadingSize? Size { get; set; }

    /// <summary>The heading level, 1 to 6. Unset, it is not part of the document outline.</summary>
    public int? Level { get; set; }

    /// <summary>Draws it in the accent colour.</summary>
    public bool? Accent { get; set; }

    /// <inheritdoc />
    protected override string TagName => Level switch
    {
        1 => "h1",
        2 => "h2",
        3 => "h3",
        4 => "h4",
        5 => "h5",
        6 => "h6",
        _ => "div",
    };

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "font-medium",
            Accent == true ? "text-fx-accent-content" : "text-zinc-800 dark:text-white",
            SizeClass(Size),
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => UiDataMarker.Join(Marker, Data);

    private static string SizeClass(Ui.HeadingSize? size) => size switch
    {
        Ui.HeadingSize.Lg => "text-base",
        Ui.HeadingSize.Xl => "text-2xl",
        Ui.HeadingSize.Xxl => "text-4xl",
        _ => "text-sm",
    };

    // For the kit's own titled components (a card, a page header), which draw their heading themselves: one
    // element per level, built by the chain entry for that tag.
    internal static Func<string, Component> Element(int? level) => level switch
    {
        1 => classes => H1.Class(classes),
        2 => classes => H2.Class(classes),
        3 => classes => H3.Class(classes),
        4 => classes => H4.Class(classes),
        5 => classes => H5.Class(classes),
        6 => classes => H6.Class(classes),
        _ => classes => Div.Class(classes),
    };
}
