using static Rask.Markup;

namespace Rask;

/// <summary>
///     What one option of a pillbox holds, as Flux lays it out: the tick's place, then its words or its content.
/// </summary>
internal static class UiPillboxRow
{
    /// <summary>The content of <paramref name="option" />'s row.</summary>
    /// <param name="option">The option.</param>
    internal static Component?[] Content(UiSelectOption option) =>
    [
        Div.Class(UiPillboxLook.CheckSlot)[Ui.Icon.Name(Ui.IconName.Check).Mini.Class(UiPillboxLook.Check)],
        .. option.HasCustomContent ? option.Children! : [option.Text],
    ];
}
