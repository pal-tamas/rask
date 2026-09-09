namespace Rask.Ui;

/// <summary>
/// Events in order.
/// </summary>
public sealed partial class UiTimeline : Ul
{

    /// <summary>Runs down the page rather than across it.</summary>
    public bool? Vertical { get; set; }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "timeline",
            Vertical == true ? "timeline-vertical" : "timeline-horizontal",
            Class);
}
