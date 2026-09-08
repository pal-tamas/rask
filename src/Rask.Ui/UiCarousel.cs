namespace Rask.Ui;

/// <summary>
/// A row that scrolls sideways, one item at a time.
/// </summary>
/// <remarks>
/// CSS scroll-snap, so the swiping is the browser's — no script, and it keeps the momentum and the
/// scrollbar a native scroller has. Each child should carry <c>carousel-item</c>.
/// </remarks>
public sealed partial class UiCarousel : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("carousel", Class))[Children ?? []];
}
