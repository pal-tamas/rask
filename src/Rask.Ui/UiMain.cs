namespace Rask;

/// <summary>Flux's <c>flux:main</c>: the page's content, beside the sidebar and under the header.</summary>
/// <remarks>
/// <para>
/// Whatever holds it — the body, or a wrapper — becomes the layout grid: <see cref="UiHeader" /> across the
/// top, <see cref="UiSidebar" /> down the side, this in what is left. Keep the three direct siblings.
/// </para>
/// <para>
/// A <c>&lt;div&gt;</c>, as Flux writes it. Put a <c>&lt;main&gt;</c> inside for the landmark Rask focuses after a navigation.
/// </para>
/// </remarks>
public sealed partial class UiMain : Component
{
    /// <summary>
    ///     Draws the content on a bordered, rounded panel with a gutter round it. From <c>lg</c> up the layout
    ///     fills the viewport and the panel scrolls inside; below it the panel is the full width and the page scrolls.
    /// </summary>
    public bool? Inset { get; set; }

    /// <summary>Holds the content to the container width, centred — inside the panel, when <see cref="Inset" />.</summary>
    public bool? Container { get; set; }

    /// <summary>Classes for the content area.</summary>
    public string? Class { get; set; }

    private const string Panel =
        "min-h-0 min-w-0 bg-white p-6 lg:m-2 lg:overflow-y-auto lg:overscroll-contain lg:rounded-xl lg:border lg:border-zinc-200 "
        + "lg:p-10 lg:shadow-xs dark:bg-zinc-800 dark:lg:border-zinc-700 [grid-area:main]";

    /// <inheritdoc />
    protected override Component? Render() => (Inset == true, Container == true) switch
    {
        (true, true) => Div.Class(UiClass.Compose(Panel, Class)).Attributes(("data-ui-main", ""), ("data-inset", ""))[
            Div.Class("mx-auto [:where(&)]:max-w-7xl")[Children ?? []]
        ],
        (true, false) => Div.Class(UiClass.Compose(Panel, Class)).Attributes(("data-ui-main", ""), ("data-inset", ""))[
            Children ?? []
        ],
        (false, true) => Div.Class(UiClass.Compose("mx-auto w-full p-6 lg:p-8 [grid-area:main] [:where(&)]:max-w-7xl", Class))
            .Attributes(("data-ui-main", ""))[Children ?? []],
        _ => Div.Class(UiClass.Compose("p-6 lg:p-8 [grid-area:main]", Class)).Attributes(("data-ui-main", ""))[Children ?? []],
    };
}
