namespace Rask.Dashboard.Panels;

/// <summary>
/// One row of a queue table, projected to the shape the outbox, jobs and mail tables share. Keeping the
/// panel generic over this rather than over the three entity types is what lets a single page serve all
/// three — they differ only in which columns exist, not in what an operator needs to see.
/// </summary>
/// <param name="Id">The row's key.</param>
/// <param name="Type">The registered type name (or, for mail, the subject).</param>
/// <param name="Summary">A one-line description — the recipient list for mail, the payload preview otherwise.</param>
/// <param name="CreatedAt">When the row was enqueued (UTC).</param>
/// <param name="RunAt">When it next becomes eligible (UTC). Equal to <paramref name="CreatedAt"/> for the outbox, which has no delay.</param>
/// <param name="ProcessedAt">When it completed (UTC), or <c>null</c> while outstanding.</param>
/// <param name="Attempts">How many times it has been tried.</param>
/// <param name="Error">The last failure message.</param>
/// <param name="Payload">The stored payload, for the drill-down.</param>
public sealed record QueueRow(
    long Id,
    string Type,
    string Summary,
    DateTime CreatedAt,
    DateTime RunAt,
    DateTime? ProcessedAt,
    int Attempts,
    string? Error,
    string Payload);
