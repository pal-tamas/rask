namespace Rask.Values;

/// <summary>
///     Which parts of an <see cref="Allocation.Split(decimal, IReadOnlyList{decimal}, decimal, Remainder)" /> take what
///     rounding every share down to the step left over — one step each, until none is left.
/// </summary>
public enum Remainder
{
    /// <summary>The first parts, in the order given: what is left goes one step each to the first ones.</summary>
    ToFirst,

    /// <summary>The last parts, from the last one back.</summary>
    ToLast,

    /// <summary>
    ///     The parts that lost the most to the rounding (the largest-remainder method), the earlier one first on a tie: each
    ///     share ends as close to its exact part as the step allows.
    /// </summary>
    ToLargest,
}
