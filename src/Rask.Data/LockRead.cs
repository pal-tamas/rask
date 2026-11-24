namespace Rask.Data;

/// <summary>How a declared lock (<see cref="LockedByBuilderExtensions" />) reads the parents of a save.</summary>
public enum LockRead
{
    /// <summary>A plain read: what the parents are as the save begins.</summary>
    Plain,

    /// <summary>
    ///     Under an update lock held to the end of the save's transaction — <c>UPDLOCK, HOLDLOCK</c> on SQL Server,
    ///     <c>FOR UPDATE</c> on PostgreSQL; SQLite's one writer serializes the save already — so a parent cannot be
    ///     locked between the check and the write. The save runs in a transaction of its own when its caller opened none.
    /// </summary>
    ForUpdate,
}
