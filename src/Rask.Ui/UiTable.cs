using System.Text;

namespace Rask.Ui;

/// <summary>
/// A table of data, styled by the kit. It IS the <c>&lt;table&gt;</c> element.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="UiElement" />, so every step an element takes — <c>Id</c>, <c>Class</c>, <c>Style</c>,
/// <c>Data</c>, <c>Role</c>, <c>Aria</c>, the events — works on it with nothing redeclared, and a call site's
/// <c>.Class("mb-0")</c> is added to the kit's classes rather than replacing them.
/// </para>
/// <para>
/// The cell padding lives HERE, which the previous version's documentation claimed and its markup did not
/// do: every console table padded each <c>th</c> and <c>td</c> by hand. It is the same <c>px-3 py-2</c>
/// those cells carried, so a table that still pads its own cells renders exactly as before.
/// </para>
/// <para>
/// It is a rule in the kit's stylesheet, on the <c>ui-table</c> marker, rather than a <c>[&amp;_td]:px-3</c>
/// variant here, and that is for the cell that wants something else. A descendant variant is more specific
/// than the <c>px-0</c> a cell writes on itself and silently beats it; the kit's layer sits below the app's
/// utilities, so the cell's own class wins.
/// </para>
/// </remarks>
public sealed partial class UiTable : UiElement
{
    /// <summary>
    ///     A complete literal, so the kit's Tailwind build can see every class — a composed name is invisible
    ///     to the scan and silently emits nothing.
    /// </summary>
    private const string Base = "ui-table w-full border-collapse text-left text-sm";

    private const string ScrollBox = "overflow-x-auto rounded-xl border border-base-300 bg-base-100";

    /// <summary>
    ///     Puts the table in a bordered box that scrolls sideways when the table is wider than its column,
    ///     instead of pushing the whole page wide.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The table keeps everything the call site set — its id, classes, data, ARIA and handlers are on the
    ///     <c>&lt;table&gt;</c>, and the box carries only its own classes — so a selector such as
    ///     <c>#orders tbody tr</c> and a screen reader's view of the table are the same either way.
    ///     </para>
    ///     <para>
    ///     The scroll is a backstop, not the mobile plan. A table someone has to swipe sideways to read has
    ///     hidden the column they came for, so a page drops its secondary columns below <c>sm</c>
    ///     (<c>hidden sm:table-cell</c>) and lets the first cell carry the stacked detail.
    ///     </para>
    /// </remarks>
    public bool? Scroll { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     None while scrolling. An element's children are written straight from the indexer, so the table
    ///     cannot put a box around itself as an element; it renders as a component instead, whose output is
    ///     the box with the table inside.
    /// </remarks>
    protected override string? TagName => Scroll == true ? null : "table";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Base, Class);

    /// <inheritdoc />
    protected override Component? Render() =>
        Scroll == true ? Div.Class(ScrollBox)[ScrolledTable.Owner(this)[Children ?? []]] : this;

    /// <summary>
    ///     This table's own attribute walk, for the <c>&lt;table&gt;</c> the scroll box renders around its
    ///     children.
    /// </summary>
    internal void WriteTableAttributes(StringBuilder sb) => WriteAttributes(sb);
}

/// <summary>
///     The <c>&lt;table&gt;</c> inside a scrolling <see cref="UiTable" />, carrying that table's attributes.
/// </summary>
/// <remarks>
///     Writing the owner's walk rather than copying its properties across is the point: a copy would have to
///     name every attribute and every event an element can carry, and would silently drop the next one Core
///     adds.
/// </remarks>
internal sealed partial class ScrolledTable : Element
{
    public required UiTable Owner { get; set; }

    /// <inheritdoc />
    protected override string TagName => "table";

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb) => Owner.WriteTableAttributes(sb);
}
