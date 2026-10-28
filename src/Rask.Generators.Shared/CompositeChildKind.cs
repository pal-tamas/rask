namespace Rask.Generators.Shared;

/// <summary>How an aggregate's form carries a child keyed by several columns.</summary>
internal enum CompositeChildKind
{
    /// <summary>A link — the holder and one other part, nothing else: a list of that part's ids, <c>ActivityIds</c>.</summary>
    Ids,

    /// <summary>A row with fields of its own: a list of rows, each its key's other parts and its fields.</summary>
    Rows,
}
