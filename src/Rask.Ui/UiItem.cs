namespace Rask;

/// <summary>
///     A row with a heading, a line of supporting text and a control at the end. Flux UI's <c>item</c>.
/// </summary>
/// <remarks>
///     <para>
///         Holds an optional <see cref="UiItemMedia" />, a <see cref="UiItemHeading" />, an optional
///         <see cref="UiItemSubheading" /> and an optional <see cref="UiItemActions" />, in any order:
///         <c>Ui.Item[Ui.ItemHeading["Seats"], Ui.ItemActions[Ui.Text["3 of 5 used"]]]</c>. In a
///         <see cref="UiItemGroup" /> it draws only the rule under it; alone it draws the group's surface itself.
///     </para>
///     <para>
///         A grid of three columns (media, text, actions) whose rows centre the media and the actions on the heading
///         and subheading, and at least a small button tall, so rows without a subheading stay even. It is a size
///         container: under 24rem of its own width a button with words in its actions moves under the text, while a
///         switch, a value, a menu or an icon button stays at the end — in a modal or a sidebar as on a phone.
///     </para>
/// </remarks>
public sealed partial class UiItem : Component
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal) { ["ui-item"] = null };

    private const string Grid =
        "[:where(&)]:grid grid-cols-[auto_1fr_auto] grid-rows-[1fr_auto_auto_auto_1fr] box-content [:where(&)]:min-h-8 "
        + "[:where(&)]:py-3 [:where(&)]:ps-4 [:where(&)]:pe-3.5 @container";

    // Built once each: an item in a group draws the rule under it, and none under the group's last.
    private static readonly string Alone = UiClass.Compose(Grid, UiItemSurface.Classes);

    private static readonly string Grouped = UiClass.Compose(Grid, "not-last:border-b border-zinc-900/5 dark:border-white/10");

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(Context.Has<UiItemGroupScope>() ? Grouped : Alone, Class).Data(Marks)[Children ?? []];
}
