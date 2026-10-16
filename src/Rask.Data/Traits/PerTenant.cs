namespace Rask.Data;

/// <summary>The aggregate is partitioned by tenant: a <c>TenantId</c>, a query filter, and the tenant stamped on add.</summary>
public abstract class PerTenant : IAggregateTrait;
