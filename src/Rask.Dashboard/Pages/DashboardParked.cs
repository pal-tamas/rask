namespace Rask.Dashboard.Pages;

/// <summary>
/// The notice a panel shows while its poll loop is parked, with the button that resumes it. Renders
/// nothing when the loop is running.
/// </summary>
internal sealed partial class DashboardParked : Component
{
    public bool Parked { get; set; }

    public Callback Resume { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Parked
            ? Ui.Alert[
                Span["Live updates paused to keep the database free."],
                Ui.Button.Size(Ui.Size.Sm).OnClick(ResumeAsync)["Resume"]
            ]
            : null;

    private Task ResumeAsync() => Resume.Invoke().AsTask();
}
