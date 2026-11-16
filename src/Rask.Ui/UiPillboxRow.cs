using static Rask.Markup;

namespace Rask;

/// <summary>
///     What one option of a pillbox holds, as Flux lays it out: the tick's place, then its words or its content.
/// </summary>
internal static class UiPillboxRow
{
    /// <summary>The content of <paramref name="option" />'s row.</summary>
    /// <param name="option">The option.</param>
    /// <param name="oneLine">A row of a windowed list: its words in one line, cut short.</param>
    internal static Component?[] Content(UiSelectOption option, bool oneLine = false) =>
        oneLine
            ? [Tick(), Span.Class(UiListboxLook.OneLineItem)[Words(option)]]
            : [Tick(), .. Words(option)];

    private static Component Tick() => Div.Class(UiPillboxLook.CheckSlot)[Ui.Icon.Name(Ui.IconName.Check).Mini.Class(UiPillboxLook.Check)];

    private static IEnumerable<Component?> Words(UiSelectOption option) => option.HasCustomContent ? option.Children! : [option.Text];
}
