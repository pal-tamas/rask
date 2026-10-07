namespace Rask;

/// <summary>
/// A card's title. Flux UI's <c>card.heading</c>.
/// </summary>
/// <remarks>
/// Without a <see cref="Level" /> it is a <c>&lt;div&gt;</c>: a title that is not part of the document outline.
/// Its line is as tall as the card's size makes it, which is what a header's actions centre on.
/// </remarks>
public sealed partial class UiCardHeading : Component
{
    /// <summary>How big it looks.</summary>
    public Ui.CardHeadingSize? Size { get; set; }

    /// <summary>The heading level to render, such as <c>2</c> for an <c>&lt;h2&gt;</c>.</summary>
    public int? Level { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var size = Size ?? Ui.CardHeadingSize.Base;
        var roomy = Context.Get<UiCardScope>()?.Roomy ?? true;
        var classes = UiClass.Compose("font-medium text-zinc-800 dark:text-white", Type(size, roomy), Class);
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-card-heading"] = null,
            ["ui-card-heading-size"] = Name(size),
            ["ui-heading"] = null,
        };

        return Element(Level)(classes, marks)[Children ?? []];
    }

    private static string Type(Ui.CardHeadingSize size, bool roomy) => size switch
    {
        Ui.CardHeadingSize.Lg => "text-base/6",
        Ui.CardHeadingSize.Xl => "text-2xl/8",
        _ => roomy ? "text-sm/6" : "text-sm/5",
    };

    private static string Name(Ui.CardHeadingSize size) => size switch
    {
        Ui.CardHeadingSize.Lg => "lg",
        Ui.CardHeadingSize.Xl => "xl",
        _ => "base",
    };

    // One element per level, built by the chain entry for that tag, so each is a real <hN> the serializer knows.
    private static Func<string, IReadOnlyDictionary<string, string?>, Component> Element(int? level) => level switch
    {
        1 => static (classes, marks) => H1.Class(classes).Data(marks),
        2 => static (classes, marks) => H2.Class(classes).Data(marks),
        3 => static (classes, marks) => H3.Class(classes).Data(marks),
        4 => static (classes, marks) => H4.Class(classes).Data(marks),
        5 => static (classes, marks) => H5.Class(classes).Data(marks),
        6 => static (classes, marks) => H6.Class(classes).Data(marks),
        _ => static (classes, marks) => Div.Class(classes).Data(marks),
    };
}
