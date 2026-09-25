namespace Rask.Core.Live;

/// <summary>
///     Kind of edit operation the diff codec emits when comparing two
///     <see cref="RenderFrame" /> streams. Maps to a verb the client interpreter
///     applies to its DOM-mirroring frame stream.
/// </summary>
public enum EditOpKind : byte
{
    /// <summary>
    ///     Set or replace an attribute's value on the element at
    ///     <see cref="EditOp.Path" />. <see cref="EditOp.Name" /> is the attribute name,
    ///     <see cref="EditOp.Value" /> is the new value (null for bare attributes).
    /// </summary>
    SetAttribute = 1,

    /// <summary>
    ///     Remove an attribute by name from the element at
    ///     <see cref="EditOp.Path" />.
    /// </summary>
    RemoveAttribute = 2,

    /// <summary>
    ///     Replace the text content of the text-or-raw node at
    ///     <see cref="EditOp.Path" />.
    /// </summary>
    UpdateText = 3,

    /// <summary>
    ///     Insert a new subtree at <see cref="EditOp.Path" /> (the index of the
    ///     slot among the parent's existing DOM children; ops further into the same
    ///     parent reference subsequent indices). The inserted markup travels as the
    ///     <see cref="EditOp.HtmlStart" />/<see cref="EditOp.HtmlEnd" /> char range into the render
    ///     HTML (sliced into the wire payload at write time), or as a verbatim
    ///     <see cref="EditOp.Value" /> string for directly-constructed ops.
    /// </summary>
    InsertSubtree = 4,

    /// <summary>
    ///     Remove a contiguous run of <see cref="EditOp.Length" /> sibling
    ///     subtrees starting at <see cref="EditOp.Path" />.
    /// </summary>
    RemoveSubtree = 5,

    /// <summary>
    ///     Move an existing sibling DOM node within its parent. <see cref="EditOp.Path" />
    ///     resolves to the destination slot among the parent's DOM-relevant children;
    ///     <see cref="EditOp.Length" /> is the source slot. The client detaches the node at the
    ///     source, then inserts at the destination slot in the post-detach sibling list — both
    ///     indexes are computed against the live DOM as it stands when this op runs (with any
    ///     preceding ops already applied). Preserves DOM identity (focus, IDL property state, event
    ///     listeners, iframe document state) since moving an existing node via
    ///     <c>parent.insertBefore</c> doesn't materialise a new element.
    /// </summary>
    MoveSubtree = 6,

    /// <summary>
    ///     A batch of sibling moves under a single keyed parent. <see cref="EditOp.Path" />
    ///     resolves to the shared parent node; <see cref="EditOp.Moves" /> is a flat
    ///     <c>[dst0, src0, dst1, src1, …]</c> array, replayed in order with identical semantics to a
    ///     run of <see cref="MoveSubtree" /> ops (detach the source slot, then insert at the
    ///     destination slot in the post-detach sibling list). The order is load-bearing: each
    ///     dst/src pair is computed against the live DOM as mutated by all preceding pairs in the
    ///     batch, so it must not be reordered. Collapses N per-row moves (each re-emitting the full
    ///     parent path) into one op + one path — the dominant wire-bytes cost of a keyed-list
    ///     reorder. Like <see cref="MoveSubtree" /> it only ever comes from the keyed path and
    ///     preserves DOM identity.
    /// </summary>
    PermutationBatch = 7,

    /// <summary>
    ///     Reconcile the CHILDREN of the element at <see cref="EditOp.Path" /> against a fresh HTML
    ///     fragment (that element's new inner HTML) via the client's <c>morph()</c> engine. Emitted
    ///     when a sibling level mixes a <see cref="RenderFrameKind.Raw" /> frame with other siblings
    ///     and changed: a Raw's verbatim markup parses into an unknown number of DOM nodes, so the
    ///     positional <c>childNodes[slot]</c> paths of the following siblings can't be trusted — but
    ///     the Raw-owning PARENT is still addressable by a clean path (every ancestor level is
    ///     untainted by construction, else the morph would have been emitted higher up). So instead of
    ///     bailing the whole render to a full-document morph, we localise it to this one parent. The
    ///     fragment travels as the <see cref="EditOp.HtmlStart" />/<see cref="EditOp.HtmlEnd" /> char
    ///     range into the render HTML (sliced at wire-write time, like
    ///     <see cref="InsertSubtree" />), or as a verbatim <see cref="EditOp.Value" /> string. Always
    ///     <see cref="EditOp.Trusted" /> — a morph handles arbitrary node counts and preserves keyed /
    ///     focus / IDL state — so it ships as a diff rather than routing to the full-HTML fallback.
    /// </summary>
    MorphSubtree = 8
}
