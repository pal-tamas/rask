namespace Rask.Dashboard.Pages;

/// <summary>The placeholder a panel shows while its first read is in flight.</summary>
/// <remarks>One place for the words, so every panel waits in the same voice: Flux's spinning icon and a line of text.</remarks>
internal sealed partial class DashboardLoading : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex items-center gap-2").Role("status")[
            Ui.Icon.Name(Ui.IconName.Loading).Variant(Ui.IconVariant.Mini).Class("text-zinc-400 dark:text-white/50"),
            Ui.Text["Reading…"]
        ];
}
