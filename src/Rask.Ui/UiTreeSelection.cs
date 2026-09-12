namespace Rask.Ui;

/// <summary>
///     What a <see cref="UiTree{T,TKey}" /> lets a reader select.
/// </summary>
/// <remarks>
///     Unset means <see cref="Single" /> as soon as the tree is given a selection or a selection handler, and
///     <see cref="None" /> otherwise: a tree that is handed <c>Selected</c> and ignores it would be a silent no-op, and
///     naming the mode as well is the kind of second line a call site forgets.
/// </remarks>
public enum UiTreeSelection
{
    /// <summary>Nothing is selectable; the tree is for expanding and reading.</summary>
    None,

    /// <summary>One node at a time. Selecting another replaces it.</summary>
    Single,

    /// <summary>Any number of nodes; Enter and Space toggle the node at the cursor.</summary>
    Multiple,
}
