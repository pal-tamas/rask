using System.Runtime.InteropServices;

namespace Rask.Core.Virtualization;

/// <summary>The rows a scroll box draws: from <paramref name="Start" />, <paramref name="Count" /> of them, between two spacers.</summary>
/// <param name="Start">The first row drawn.</param>
/// <param name="Count">How many rows are drawn.</param>
/// <param name="OffsetBefore">The room above the first, in pixels.</param>
/// <param name="OffsetAfter">The room below the last, in pixels.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct VirtualWindow(int Start, int Count, int OffsetBefore, int OffsetAfter)
{
    /// <summary>One past the last row drawn.</summary>
    internal int End => Start + Count;
}
