using System.Runtime.InteropServices;

namespace Rask.Dashboard.Panels;

/// <summary>
/// The counts an operator reads at a glance. <see cref="Failed"/> is the headline: processed climbing is
/// normal, but a system that retries itself to death still reports a healthy processed count.
/// </summary>
/// <param name="Due">Eligible to run now.</param>
/// <param name="Delayed">Waiting on a backoff or a scheduled time.</param>
/// <param name="Failed">Out of attempts and still unprocessed — dead letters.</param>
/// <param name="Processed">Completed.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct QueueCounts(int Due, int Delayed, int Failed, int Processed)
{
    /// <summary>Everything not yet processed, whatever the reason.</summary>
    public int Outstanding => Due + Delayed + Failed;
}
