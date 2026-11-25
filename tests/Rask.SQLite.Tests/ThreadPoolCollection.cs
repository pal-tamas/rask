namespace Rask.SQLite.Tests;

/// <summary>
///     The classes that resize the process's thread pool, run alone — after every other test, with nothing beside
///     them — so a shrunken pool neither starves another test nor is starved by one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreadPoolCollection
{
    public const string Name = "thread-pool";
}
