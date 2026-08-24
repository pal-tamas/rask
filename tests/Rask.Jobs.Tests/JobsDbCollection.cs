namespace Rask.Jobs.Tests;

/// <summary>
///     The classes that build a <see cref="JobsDbContext" />, run as one xUnit collection so they do not
///     run in parallel with each other.
/// </summary>
/// <remarks>
///     <para>
///         EF Core's model cache is <b>per process, keyed on the context type</b> — not per
///         <c>ServiceProvider</c>. Each of these classes builds its own provider over its own SQLite
///         file and they still share one <c>IModelSource</c> and one <c>IModel</c> instance, so the
///         first test in each class racing to first-touch <c>JobsDbContext</c>'s model is two threads
///         driving one piece of EF-internal state. That is the shape that made the Outbox suite fail
///         the gate in #769 with "The model must be finalized and its runtime dependencies must be
///         initialized before 'GetRelationalModel' can be used", on a diff that touched none of it.
///     </para>
///     <para>
///         Unobserved here, and serialised anyway: the failure is not reproducible in isolation — a
///         targeted 16-thread repro of the Outbox one stayed green about thirty times, idle and under
///         load, and only appeared on the full-solution gate. Waiting for it to be observed means
///         waiting for a red gate on an unrelated diff.
///     </para>
///     <para>
///         It costs the suite nothing measurable: these are a handful of SQLite integration tests, and
///         the ones with real budgets spend their time waiting rather than working. Tests <i>within</i>
///         a class already ran serially, so nothing here changes their meaning.
///     </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class JobsDbCollection
{
    public const string Name = "jobs-db";
}
