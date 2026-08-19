namespace Rask.Outbox.Tests;

/// <summary>
///     The classes that build an <see cref="OutboxDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other.
/// </summary>
/// <remarks>
///     <para>
///         EF Core's model cache is <b>per process, keyed on the context type</b> — not per
///         <c>ServiceProvider</c>. Two of these classes each build their own provider over their own
///         SQLite file, and they still share one <c>IModelSource</c> and one <c>IModel</c> instance
///         (verified by reference identity). So the first test in each class racing to first-touch
///         <c>OutboxDbContext</c>'s model is two threads driving one piece of EF-internal state, and the
///         gate caught it doing so: <c>SaveChangesAsync</c> threw "The model must be finalized and its
///         runtime dependencies must be initialized before 'GetRelationalModel' can be used" on a diff
///         that touched none of this (#769).
///     </para>
///     <para>
///         Serialising the classes removes the concurrency the failure needs, which no amount of
///         retrying or widening a timeout would. It costs little: these are a handful of SQLite
///         integration tests, and the ones with real budgets (the shutdown-grace pair) spend their time
///         waiting rather than working, so the suite is bounded by those waits either way. Tests
///         <i>within</i> a class already ran serially, so nothing here changes their meaning.
///     </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class OutboxDbCollection
{
    public const string Name = "outbox-db";
}
