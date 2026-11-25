namespace Rask.Data.Tests;

/// <summary>
///     The classes whose subject IS the process-wide database binding — <c>Db.Configure</c> itself, the browser host that
///     calls it, and <c>TestDatabase.FromEnvironment</c>, which binds the process because it is opened after an await —
///     which run alone, after every parallel class.
/// </summary>
/// <remarks>
///     Every other class binds the model surface to its own database for its own flow (<c>TestDatabase.Start</c>,
///     <c>Db.Use</c>), which a process-wide binding never overrides, so they run side by side. These point the whole
///     process somewhere and unconfigure it again, and a class asserting there is no database at all
///     (<c>StoreRulesBeforeTheSaveTests</c>) would see theirs; parallelization is disabled outright, as that is not a
///     race between these classes but with every other one.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideDbCollection
{
    public const string Name = "process-wide-db";
}
