namespace Rask;

/// <summary>
///     An item's title. Flux UI's <c>item.heading</c>.
/// </summary>
/// <remarks>
///     14px medium zinc-800 (white in dark). Without a <see cref="Level" /> it is a <c>&lt;div&gt;</c>: a row's title
///     that is not part of the document outline.
/// </remarks>
public sealed partial class UiItemHeading : UiElement
{
    private static readonly UiPartMarker Marker = new UiPartMarker("ui-item-heading").And("ui-heading");

    /// <summary>The heading level to render, such as <c>3</c> for an <c>&lt;h3&gt;</c>.</summary>
    public int? Level { get; set; }

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
        UiClass.Compose("col-start-2 row-start-2 [:where(&)]:min-w-0 wrap-break-word [:where(&)]:text-sm font-medium [:where(&)]:text-zinc-800 dark:text-white", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
