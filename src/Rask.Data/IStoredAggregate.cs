namespace Rask.Data;

/// <summary>
///     Whether an aggregate is known to have a row: read from the store, or handed to a save. One that is not was made
///     by code — its factory, a <c>new()</c> — so its first <c>Save()</c> inserts it with no read first.
/// </summary>
/// <remarks>
///     Marked by <see cref="WritableChildrenInterceptor" /> on materialization and at every save, and by
///     <see cref="AggregateSave" /> when it remembers what it read. A context without that interceptor marks nothing,
///     and a save through it always reads first.
/// </remarks>
internal interface IStoredAggregate
{
    /// <summary>Whether the aggregate was read from the store or handed to a save.</summary>
    bool IsStored { get; }

    /// <summary>Records that the aggregate has, or is being given, a row.</summary>
    void MarkStored();
}
