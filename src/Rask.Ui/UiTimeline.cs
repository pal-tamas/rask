namespace Rask.Ui;

/// <summary>
/// Events in order.
/// </summary>
public sealed partial class UiTimeline : Component
{
    /// <summary>Runs down the page rather than across it.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "timeline",
            Vertical == true ? "timeline-vertical" : "timeline-horizontal",
            Class))[Children ?? []];
}
