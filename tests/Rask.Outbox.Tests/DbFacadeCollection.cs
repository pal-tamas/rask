namespace Rask.Outbox.Tests;

/// <summary>The classes that point the static <c>Db</c> facade at their own database, which take turns.</summary>
/// <remarks>
///     <c>Db.Configure</c> keeps its factory in a static, and <c>TestDatabase.Start</c> configures it too — so a class
///     configuring it beside one of those would open its <c>Db.Transaction</c> on whichever database was configured last,
///     and <c>Db.Reset</c> in one unconfigures the other mid-test. It is that static the collection serialises, not the
///     database: each class's database is its own, in memory.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DbFacadeCollection
{
    public const string Name = "db-facade";
}
