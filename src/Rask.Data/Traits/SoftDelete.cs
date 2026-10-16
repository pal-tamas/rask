namespace Rask.Data;

/// <summary>The aggregate gets a generated <c>X.Delete(id)</c> that stamps <c>DeletedAt</c> instead, and every read hides the row; <c>IgnoreQueryFilters()</c> lists it again.</summary>
public abstract class SoftDelete : IAggregateTrait;
