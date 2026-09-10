namespace Rask.Benchmarks.Sqlite;

/// <summary>
/// Watches the <c>-wal</c> sidecar's size. Sampled by the runner between windows and never from a virtual
/// user — a VU that stopped to stat a file would be recording its own file I/O as SQLite latency.
/// <para>
/// It deliberately never issues <c>PRAGMA wal_checkpoint</c>: that takes locks and would perturb the very
/// thing being measured. Checkpoint stalls are inferred instead from an isolated per-window tail spike
/// (p99.9/max up, p50 flat).
/// </para>
/// </summary>
internal sealed class WalMonitor(LoadScenario scenario)
{
    internal long MaxWalBytes { get; private set; }

    internal void Sample()
    {
        var wal = new FileInfo($"{scenario.DbPath}-wal");
        if (wal.Exists && wal.Length > MaxWalBytes)
        {
            MaxWalBytes = wal.Length;
        }
    }

    internal long DbBytes()
    {
        var db = new FileInfo(scenario.DbPath);
        return db.Exists ? db.Length : 0;
    }
}
