namespace Rask.Auth.Tests;

/// <summary>
///     The classes that build an <see cref="AuthDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other. EF Core's model cache is per process and keyed on the
///     context type, so a per-class <c>ServiceProvider</c> over a per-class SQLite file still shares one
///     <c>IModel</c> — see <c>Rask.Cache.Tests.CacheDbCollection</c> for the same shape.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthDbCollection
{
    public const string Name = "auth-db";
}
