namespace Rask.Data;

/// <summary>The table has <c>CreatedAt</c> and <c>UpdatedAt</c>: <see cref="ICreated" /> and <see cref="IUpdated" /> together.</summary>
#pragma warning disable CA1040 // a composite: what it extends IS the declaration, read by the generator
public interface ITimestamps : ICreated, IUpdated;
#pragma warning restore CA1040
