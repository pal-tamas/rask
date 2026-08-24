namespace Rask.Cache.Tests;

/// <summary>
///     The classes that build a <see cref="CacheDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other. See <c>Rask.Jobs.Tests.JobsDbCollection</c> for why the
///     shape is a race — EF Core's model cache is per process and keyed on the context type, so a
///     per-class <c>ServiceProvider</c> over a per-class SQLite file still shares one <c>IModel</c>.
/// </summary>
/// <remarks>
///     <c>CacheOptionsTests</c> is deliberately not in the collection: it only calls
///     <c>AddRaskCache&lt;CacheDbContext&gt;</c> to assert the option validation, and never builds a
///     context — so it never touches the model and has nothing to race over.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class CacheDbCollection
{
    public const string Name = "cache-db";
}
