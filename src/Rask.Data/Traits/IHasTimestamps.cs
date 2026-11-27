namespace Rask.Data;

/// <summary>
/// The table has <c>CreatedAt</c> and <c>UpdatedAt</c>: <see cref="IHasCreatedAt" /> and <see cref="IHasUpdatedAt" />
/// together. <c>Product : Aggregate&lt;ProductId&gt;, IHasTimestamps</c>.
/// </summary>
public interface IHasTimestamps : IHasCreatedAt, IHasUpdatedAt;
