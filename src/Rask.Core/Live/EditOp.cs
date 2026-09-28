namespace Rask.Core.Live;

/// <summary>
///     A single edit operation produced by <c>FrameDiffer.Diff</c>. Each op
///     names the DOM node it targets via <see cref="Path" /> — a sequence of child
///     indices from the document root, counting only DOM-relevant nodes (elements,
///     text, raw, doctype). Attribute frames in the underlying render-tree stream
///     are NOT counted; Component frames are transparent (their rendered body
///     contributes siblings at the surrounding level). The path representation lets
///     the client interpreter walk its DOM by simple <c>parent.children[i]</c>
///     descent without needing to mirror the server's frame stream.
/// </summary>
public readonly struct EditOp
{
    public EditOp(EditOpKind kind, int[] path, string? name, string? value, int length = 0, bool trusted = false,
        int[]? moves = null, int htmlStart = -1, int htmlEnd = -1)
    {
        Kind = kind;
        Path = path;
        Name = name;
        Value = value;
        Length = length;
        Trusted = trusted;
        Moves = moves;
        HtmlStart = htmlStart;
        HtmlEnd = htmlEnd;
    }

    public EditOpKind Kind { get; }

    /// <summary>
    ///     Component-index sequence from the document root that identifies the
    ///     target DOM node (or, for <see cref="EditOpKind.InsertSubtree" /> /
    ///     <see cref="EditOpKind.RemoveSubtree" /> / <see cref="EditOpKind.MoveSubtree" />,
    ///     the slot among siblings).
    /// </summary>
    public int[] Path { get; }

    public string? Name { get; }
    public string? Value { get; }
    public int Length { get; }

    /// <summary>
    ///     For <see cref="EditOpKind.InsertSubtree" />: the <c>[HtmlStart..HtmlEnd)</c> char range of
    ///     the inserted subtree's markup within the render HTML, so the wire codec can slice the
    ///     fragment straight into the UTF-8 payload at write time instead of materialising a
    ///     per-insert <see cref="Value" /> string during the diff. <c>-1</c> (the default) means no
    ///     deferred slice — the codec then ships <see cref="Value" /> verbatim (the path used by
    ///     directly-constructed ops) or null. Ignored for every other op kind.
    /// </summary>
    public int HtmlStart { get; }

    /// <summary>Companion to <see cref="HtmlStart" /> — the exclusive end of the fragment range.</summary>
    public int HtmlEnd { get; }

    /// <summary>
    ///     For <see cref="EditOpKind.PermutationBatch" /> only: a flat
    ///     <c>[dst0, src0, dst1, src1, …]</c> array of sibling moves under the parent at
    ///     <see cref="Path" />, in apply order. Null for every other op kind.
    /// </summary>
    public int[]? Moves { get; }

    /// <summary>
    ///     True when this structural op was produced by the keyed-matching path
    ///     (where the moved/inserted/removed node is identified by <c>data-rask-key</c>, so the
    ///     surrounding morph-baseline DOM state stays consistent under apply). Positional structural
    ///     ops set this to <c>false</c> and the live-session gates route them through the full-HTML
    ///     morph path. Non-structural ops (SetAttribute, RemoveAttribute, UpdateText) ignore the
    ///     flag — they're always safe to ship.
    /// </summary>
    public bool Trusted { get; }
}
