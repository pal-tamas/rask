namespace Rask.Dashboard.Panels;

/// <summary>
/// Whether the backup has been proven <b>restorable</b>, which is a different fact from whether the
/// replicator is running — a replica written to the wrong prefix keeps replication looking healthy and is
/// only ever caught by restoring it.
/// </summary>
/// <param name="Outcome">
/// The verdict, as free text so the dashboard stays provider-agnostic (it never references the backup
/// packages). "Verified" is the good one; "Inconclusive" means the check raced replication lag.
/// </param>
/// <param name="Level">
/// How the outcome should read. Supplied by the probe rather than inferred here, because only the probe
/// knows which of its own outcome names are failures.
/// </param>
/// <param name="LastVerifiedAt">
/// When the backup was last proven restorable — the field worth alerting on, since it survives a pass
/// that merely raced replication.
/// </param>
/// <param name="LastError">Why the most recent pass was inconclusive or failed.</param>
public sealed record BackupVerificationInfo(
    string Outcome, BackupVerificationLevel Level, DateTimeOffset? LastVerifiedAt, string? LastError);
