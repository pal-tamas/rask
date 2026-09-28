namespace Rask;

/// <summary>
///     One visible node of a <see cref="UiTree{T,TKey}" />, with everything its row and the keyboard need.
/// </summary>
/// <param name="Node">The app's node.</param>
/// <param name="Key">Its key.</param>
/// <param name="Level">1 for a root, one more per generation — what <c>aria-level</c> says.</param>
/// <param name="Parent">The parent's index in the same list, or <c>-1</c> for a root.</param>
/// <param name="HasChildren">Whether it can be expanded at all.</param>
/// <param name="IsExpanded">Whether its children are in the list below it.</param>
/// <param name="SetSize">How many siblings it has, itself included.</param>
/// <param name="PosInSet">Its 1-based place among them.</param>
internal readonly record struct UiTreeRow<T, TKey>(
    T Node,
    TKey Key,
    int Level,
    int Parent,
    bool HasChildren,
    bool IsExpanded,
    int SetSize,
    int PosInSet);
