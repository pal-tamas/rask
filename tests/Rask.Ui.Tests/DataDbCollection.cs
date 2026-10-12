namespace Rask.UiTests;

/// <summary>
///     The classes that point Rask.Data's process-wide <c>Db</c> at a database of their own, run as one xUnit
///     collection so that no two of them do it at once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DataDbCollection
{
    public const string Name = "data-db";
}
