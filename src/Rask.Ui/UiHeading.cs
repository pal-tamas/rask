namespace Rask;

/// <summary>
/// A heading: how big it looks, and what level it is, decided separately.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's heading. <see cref="Size" /> is the visual scale — the default for most headings, <see cref="Ui.Size.Lg" />
/// for a card or a dialog, <see cref="Ui.Size.Xl" /> for a page's hero, rarely. <see cref="Level" /> is the outline a
/// screen reader navigates by: <c>&lt;h1&gt;</c> through <c>&lt;h6&gt;</c>. They are two properties because the two go
/// wrong in opposite directions: a card title that must look small is still the second level of the page, and a
/// big number on a dashboard is not a heading at all.
/// </para>
/// <para>
/// Without a <see cref="Level" /> it renders a <c>&lt;div&gt;</c> — a title that does not belong in the outline. Give
/// one to everything that does; skipping levels is how an outline stops meaning anything.
/// </para>
/// </remarks>
public sealed partial class UiHeading : Component
{
    /// <summary>The heading level, 1 to 6. Unset, it is not part of the document outline.</summary>
    public int? Level { get; set; }

    /// <summary>How big it looks.</summary>
    public Ui.Size? Size { get; set; }

    /// <summary>Draws it in the theme's primary colour.</summary>
    public bool? Accent { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var classes = UiClass.Compose(
            "text-base-content",
            SizeClasses(Size ?? Ui.Size.Default),
            Accent == true ? "text-primary" : "",
            Class);

        return Element(Level)(classes)[Children ?? []];
    }

    // Flux's scale: the default for most headings, Lg for a card or a dialog, Xl for a page's hero. A size and a
    // weight each, so kept here rather than in UiClassNames, whose tables hold one class per entry.
    private static string SizeClasses(Ui.Size size) => size switch
    {
        Ui.Size.Xs => "text-xs font-medium",
        Ui.Size.Sm => "text-sm font-medium",
        Ui.Size.Lg => "text-base font-semibold",
        Ui.Size.Xl => "text-2xl font-semibold",
        _ => "text-sm font-semibold",
    };

    // One element per level, built by the chain entry for that tag — which is what keeps each a real <hN> the
    // serializer knows, rather than a tag name spelled into a string.
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
