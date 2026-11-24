namespace Rask.Data;

/// <summary>
///     Every save of the aggregate leaves an <see cref="AuditEntry" /> in the same save: who, when, and what it was and
///     became — its children's rows too, as <c>Lines[1].Quantity</c>.
/// </summary>
public abstract class Audited : IAggregateTrait;
