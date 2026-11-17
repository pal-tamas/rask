using System.Runtime.InteropServices;

namespace Rask.Core.Virtualization;

/// <summary>What a measured list's spacer above says to the runtime beside its window (<see cref="VirtualSpacers" />).</summary>
/// <param name="Anchor">The edge the box holds on to.</param>
/// <param name="AimSeq">The scroll to a row waiting for the browser, counted from 1; 0 for none.</param>
/// <param name="AimIndex">The row it scrolls to.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct VirtualMarks(VirtualAnchor Anchor, int AimSeq, int AimIndex)
{
    /// <summary>A list that holds the top and is scrolling nowhere.</summary>
    internal static VirtualMarks Default => new(VirtualAnchor.Start, 0, 0);
}
