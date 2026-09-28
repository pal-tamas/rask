namespace Rask.Dashboard.Pages;

/// <summary>The placeholder a panel shows while its first read is in flight.</summary>
/// <remarks>One place for the words, so every panel waits in the same voice.</remarks>
internal sealed partial class DashboardLoading : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Ui.Loading.Text("Reading…");
}
