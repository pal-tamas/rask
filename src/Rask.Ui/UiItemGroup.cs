namespace Rask;

/// <summary>
///     Items drawn as one bordered surface with a rule between each. Flux UI's <c>item.group</c>.
/// </summary>
/// <remarks>
///     A settings page's rows, an account's details, a list of people or connections:
///     <c>Ui.ItemGroup[Ui.Item[…], Ui.Item[…]]</c>. The group draws the surface (white, a 1px border, 12px corners,
///     a faint shadow; white at 10% in dark) and each <see cref="UiItem" /> in it draws only the rule under it.
/// </remarks>
public sealed partial class UiItemGroup : Component
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal) { ["ui-item-group"] = null };

    /// <summary>Classes for the call site, added to the group's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiItemSurface.Classes, Class).Data(Marks)[Context.Provide(UiItemGroupScope.Instance)[Children ?? []]];
}
