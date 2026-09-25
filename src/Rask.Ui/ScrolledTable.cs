using System.Text;

namespace Rask;

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
