using System.Runtime.InteropServices;

namespace Rask.Core.Caching;

/// <summary>How long a stored fragment lives: an absolute deadline, optionally cut short by a sliding window of disuse.</summary>
/// <param name="Absolute">How long after it is stored the entry expires, whatever happens.</param>
/// <param name="Sliding">How long it may go unread before it expires; null when only <paramref name="Absolute" /> applies.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FragmentLifetime(TimeSpan Absolute, TimeSpan? Sliding);
