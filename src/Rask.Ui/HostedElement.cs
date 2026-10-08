using System.Text;

namespace Rask;

/// <summary>
///     One element a kit part renders around or inside its children: a custom tag of its own, or — given an
///     <see cref="Owner" /> — the part's root, carrying that part's attributes.
/// </summary>
/// <remarks>
///     An element's children are written straight from the indexer, so a part that IS an element has nowhere
///     to put markup of its own between its tag and them: a header cell cannot wrap its label, a table cannot
///     sit in a scroll box. Such a part renders as a component instead, and this is the tag it would have
///     been. Writing the owner's walk rather than copying its properties across is the point — a copy would
///     have to name every attribute and every event an element can carry, and would drop the next one Core
///     adds.
/// </remarks>
internal sealed partial class HostedElement : Element
{
    public required string Tag { get; set; }

    public IUiHost? Owner { get; set; }

    /// <summary>
    ///     True when what is below this element is another writer's — the editor's surface is ProseMirror's.
    ///     The frame diff then skips the subtree and the client morph refuses to descend into it, so what is
    ///     rendered inside is only ever the first paint.
    /// </summary>
    public bool? Opaque { get; set; }

    /// <inheritdoc />
    protected override bool OpaqueSubtree => Opaque == true;

    /// <inheritdoc />
    protected override string TagName => Tag;

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Owner is { } owner)
        {
            owner.WriteHostAttributes(sb);
            return;
        }

        base.WriteAttributes(sb);
    }
}
