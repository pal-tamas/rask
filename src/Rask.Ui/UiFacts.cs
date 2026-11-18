namespace Rask;

/// <summary>
///     What a summary card says of a record, as a description list: each <see cref="Ui.Fact" /> a label over its value.
///     It IS the <c>&lt;dl&gt;</c>: <c>Ui.Card[Ui.Facts[Ui.Fact("Company", company), Ui.Fact("Driver card", card)]]</c>.
/// </summary>
/// <remarks>
///     Not Flux's: Flux has no description list, so this is composed from its typography (the owner, 2026-11-18, #1363) —
///     a label in Flux's subtle small text, its value in Flux's body text, as a <c>flux:card</c>'s own text is drawn. The
///     facts lie in as many columns of 12rem at least as the box has room for, and in one on a phone; a class of the call
///     site's (<c>.Class("sm:grid-cols-3")</c>) wins over that.
/// </remarks>
public sealed partial class UiFacts : UiElement
{
    // Columns of 12rem at least, as many as fit and sharing what is left; never wider than the box. Kit defaults, so a
    // class of the call site's (`sm:grid-cols-3`, `gap-2`, `flex`) wins.
    private const string Grid =
        "[:where(&)]:grid [:where(&)]:gap-x-6 [:where(&)]:gap-y-4 [:where(&)]:m-0 "
        + "[:where(&)]:grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))]";

    private static readonly UiPartMarker Marker = new("ui-facts");

    /// <inheritdoc />
    protected override string TagName => "dl";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Grid, Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
