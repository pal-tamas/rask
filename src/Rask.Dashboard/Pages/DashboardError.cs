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
            : Ui.Alert.Tone(Ui.Tone.Error)[
                Ui.Icon.Name(Ui.IconName.Warning),
                Span["Couldn't read: ", Message]
            ];
}
