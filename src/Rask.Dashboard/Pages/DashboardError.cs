namespace Rask.Dashboard.Pages;

/// <summary>
/// Shown when a read threw. A dashboard that silently stops updating is worse than one that says it
/// couldn't read, so the panel keeps its last values and puts the reason on top. Renders nothing when
/// there is no message, so a panel can hand it the read error unconditionally.
/// </summary>
internal sealed partial class DashboardError : Component
{
    public string? Message { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Message is null
            ? null
            : Ui.Callout.Danger.Icon(Ui.IconName.ExclamationTriangle).Role("alert").Heading("Couldn't read: ").Text(Message);
}
