namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
///     The classes that point Rask.Data's process-wide <c>Db</c> (and <c>ReadDb</c>) at a database of their own, run as
///     one xUnit collection so that no two of them do it at once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProcessWideDbCollection
{
    public const string Name = "process-wide-db";
}
