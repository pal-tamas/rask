using System.Runtime.InteropServices;

namespace Rask.Server.Hibernation;

/// <summary>How much memory is in use, of how much the process may have.</summary>
/// <param name="InUseBytes">The memory in use, as the last collection saw it.</param>
/// <param name="AvailableBytes">What the process may use: the container's limit where there is one, else the machine's.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MemoryLoad(long InUseBytes, long AvailableBytes)
{
    /// <summary>The runtime's view as of its last collection, which takes a container's limit into account.</summary>
    public static MemoryLoad Now()
    {
        var info = GC.GetGCMemoryInfo();
        return new MemoryLoad(info.MemoryLoadBytes, info.TotalAvailableMemoryBytes);
    }
}
