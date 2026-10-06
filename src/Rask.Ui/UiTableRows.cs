namespace Rask;

/// <summary>
/// The body of a <see cref="UiTable" />: Flux's <c>flux:table.rows</c>. It is the <c>&lt;tbody&gt;</c>,
/// holding a <see cref="UiTableRow" /> per record.
/// </summary>
public sealed partial class UiTableRows : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-rows");

    /// <inheritdoc />
    protected override string TagName => "tbody";

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
