using System.Runtime.InteropServices;

namespace Rask.Dashboard.Panels;

/// <summary>Cache totals for the overview tiles.</summary>
/// <param name="Entries">Rows in the cache table.</param>
/// <param name="Bytes">Total stored value bytes.</param>
/// <param name="Expired">Rows past their expiry that the purge sweep hasn't removed yet.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CacheStats(int Entries, long Bytes, int Expired);
