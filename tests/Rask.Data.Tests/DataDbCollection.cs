namespace Rask.Data.Tests;

/// <summary>
///     The classes that build a <see cref="TestDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other. See <c>Rask.Jobs.Tests.JobsDbCollection</c> for why the
///     shape is a race — EF Core's model cache is per process and keyed on the context type, so a
///     per-class <c>ServiceProvider</c> over a per-class SQLite file still shares one <c>IModel</c>.
/// </summary>
/// <remarks>
///     The extra contexts <c>BulkInsertFastPathTests</c> declares for its own cases
///     (<c>GeneratedKeyContext</c>, <c>GraphContext</c>, <c>CountingContext</c>) are used by that one
///     class only, so they are covered by xUnit already running a class's tests serially.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DataDbCollection
{
    public const string Name = "data-db";
}
