namespace Rask.Data;

/// <summary>How many rows of an aggregate <c>rask audit</c> asked.</summary>
/// <param name="Aggregate">The aggregate's name.</param>
/// <param name="Rows">The roots read.</param>
internal sealed record RuleAuditCount(string Aggregate, int Rows);
