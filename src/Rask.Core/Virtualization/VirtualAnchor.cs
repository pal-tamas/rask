namespace Rask.Core.Virtualization;

/// <summary>
///     Which edge of a virtual list the box holds on to as rows come and go — .NET's <c>VirtualizeAnchorMode</c>. The
///     flags combine: <c>Start | End</c> holds both.
/// </summary>
/// <remarks>
///     Away from the edges the rows the reader is looking at stay where they are whatever the rows above them do: the
///     browser's own scroll anchoring holds them where it has it (<c>overflow-anchor</c>), and the runtime holds them in
///     a table and in a browser without it. Needs the context's spacers (<c>ctx.Above</c>, <c>ctx.Below</c>).
/// </remarks>
[Flags]
#pragma warning disable CA1714 // owner-approved name (#1326): a flags enum named for what it is, as .NET's VirtualizeAnchorMode
public enum VirtualAnchor
#pragma warning restore CA1714
{
    /// <summary>Neither edge: the box stays where the reader left it.</summary>
    None = 0,

    /// <summary>A box at the top stays at the top when rows arrive above the first.</summary>
    Start = 1,

    /// <summary>
    ///     A box at the bottom follows the tail: it opens there, and rows added at the end are scrolled into view — until
    ///     the reader scrolls away from the bottom, and again once they are back. A log, a chat.
    /// </summary>
    End = 2,
}
