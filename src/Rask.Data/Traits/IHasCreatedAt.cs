namespace Rask.Data;

/// <summary>The table has a <c>CreatedAt</c>, stamped when the row is added.</summary>
/// <remarks>
/// A trait reads as a sentence on the class — <c>OrderLine : Entity&lt;Guid&gt;, IHasCreatedAt</c> — and nothing ever
/// implements a member of it: the build reads it, and a table carries only what it says.
/// </remarks>
#pragma warning disable CA1040 // a marker: the interface IS the declaration, read by the generator
public interface IHasCreatedAt;
#pragma warning restore CA1040
