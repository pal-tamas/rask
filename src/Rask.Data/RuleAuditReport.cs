namespace Rask.Data;

/// <summary>What <c>rask audit</c> reports: how many rows of each aggregate it asked, and every rule a stored one breaks.</summary>
/// <param name="Read">Each aggregate asked, with the rows of it read.</param>
/// <param name="Breaches">Each rule broken, with the ids of the aggregates that break it; empty when every row passes.</param>
internal sealed record RuleAuditReport(IReadOnlyList<RuleAuditCount> Read, IReadOnlyList<RuleBreach> Breaches)
{
    /// <summary>Whether every stored row passes every rule.</summary>
    public bool Clean => Breaches.Count == 0;

    /// <summary>What the audit could not ask, and why: an index whose filter is SQL. Not findings.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
