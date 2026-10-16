namespace Rask.Data;

/// <summary>
/// A trait a child entity, or a table that is not an aggregate, may name as well — only the timestamps:
/// <c>OrderLine : Entity&lt;Guid, Timestamps&gt;</c>. Everything else is the aggregate's.
/// </summary>
#pragma warning disable CA1040 // a marker: the type argument IS the declaration, read by the generator
public interface IEntityTrait : IAggregateTrait;
#pragma warning restore CA1040
