namespace Rask.Data;

/// <summary>
/// Something an aggregate asks of Rask, named as a type argument of its base:
/// <c>Order : Aggregate&lt;Guid, Timestamps, Versioned, Model, SoftDelete&gt;</c>.
/// </summary>
/// <remarks>
/// Read by the generator at compile time; nothing ever creates one. A table carries only what its base names.
/// </remarks>
#pragma warning disable CA1040 // a marker: the type argument IS the declaration, read by the generator
public interface IAggregateTrait;
#pragma warning restore CA1040
