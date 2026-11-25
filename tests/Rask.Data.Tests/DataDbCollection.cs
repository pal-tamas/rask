namespace Rask.Data.Tests;

/// <summary>
///     The classes that build a <see cref="TestDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other. See <c>Rask.Tests.Shared.DbCollectionGuard</c> for why the
///     shape is a race — EF Core's model cache is per process and keyed on the context type, so a
///     per-class <c>ServiceProvider</c> over a per-class database still shares one <c>IModel</c>.
/// </summary>
/// <remarks>
///     <para>
///         The extra contexts <c>BulkInsertFastPathTests</c> declares for its own cases
///         (<c>GeneratedKeyContext</c>, <c>GraphContext</c>, <c>CountingContext</c>) are used by that one
///         class only, so they are covered by xUnit already running a class's tests serially.
///     </para>
///     <para>
///         Each database is in memory and the test's own (<c>InMemorySqlite</c>), so nothing here is about sharing a
///         file. What still keeps these classes in one line is process-wide state the suite was written against: a
///         <c>TestDatabase.Start</c> also points the process-wide <c>Db</c>/<c>ReadDb</c> at its database, several classes
///         <c>Db.Configure</c> and <c>Db.Reset()</c> it, the current rows and the secret keeper are statics, and
///         <c>ServiceProviderBudget</c> counts every EF Core provider built, which a parallel first touch builds twice.
///         Run side by side, those failed some sixty tests.
///     </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DataDbCollection
{
    public const string Name = "data-db";
}
