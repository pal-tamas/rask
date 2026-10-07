using System.Text;

namespace Rask;

/// <summary>
/// The heading row of a <see cref="UiTable" />: Flux's <c>flux:table.columns</c>. It is the
/// <c>&lt;thead&gt;</c> and its one <c>&lt;tr&gt;</c>, holding a <see cref="UiTableColumn" /> per column.
/// </summary>
public sealed partial class UiTableColumns : UiElement, IUiHost
{
    private const string Stuck = "sticky top-0 z-20";

    private static readonly UiPartMarker Marker = new("ui-columns");

    /// <summary>Keeps the headings in view while the rows scroll under them.</summary>
    /// <remarks>
    ///     Give the heading row a background as well — <c>.Class("bg-white dark:bg-zinc-900")</c> — or the rows
    ///     show through it.
    /// </remarks>
    public bool? Sticky { get; set; }

    /// <inheritdoc />
    /// <remarks>None: it renders the <c>&lt;thead&gt;</c> with a row of its own inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    protected override string? ResolveClass() => Sticky is true ? UiClass.Compose(Stuck, Class) : Class;

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render() => HostedElement.Tag("thead").Owner(this)[Tr[Children ?? []]];

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);
}
