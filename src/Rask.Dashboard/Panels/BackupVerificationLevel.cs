namespace Rask.Dashboard.Panels;

/// <summary>
/// How a verification outcome reads on the dashboard. Three states, not two: "the check raced replication
/// lag" and "the restore did not contain what it should" are different, and showing the first one in red
/// is how an operator learns to ignore the tile.
/// </summary>
public enum BackupVerificationLevel
{
    /// <summary>Proven restorable.</summary>
    Verified,

    /// <summary>Nothing was proven either way — lag, or a pass that had nothing to check.</summary>
    Unknown,

    /// <summary>The backup could not be restored. This is the one worth waking someone for.</summary>
    Broken,
}
