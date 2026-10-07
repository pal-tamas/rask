namespace Rask;

/// <summary>
/// One record of a <see cref="UiTable" />: Flux's <c>flux:table.row</c>. It is the <c>&lt;tr&gt;</c>, holding
/// a <see cref="UiTableCell" /> per column.
/// </summary>
/// <remarks>
/// Flux's <c>key</c> is the <c>Key</c> every component has: give a row its record's id when the rows can be
/// reordered, so each keeps its own state.
/// </remarks>
public sealed partial class UiTableRow : UiElement
{
    private const string Stuck = "sticky top-0 z-20";

    private static readonly UiPartMarker Marker = new("ui-row");

    /// <summary>Keeps the row in view while the others scroll under it.</summary>
    public bool? Sticky { get; set; }

    /// <inheritdoc />
    protected override string TagName => "tr";

    /// <inheritdoc />
    protected override string? ResolveClass() => Sticky is true ? UiClass.Compose(Stuck, Class) : Class;

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
