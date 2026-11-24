namespace Rask.Data;

/// <summary>
///     The one spelling of SQL Server's application lock, <c>sp_getapplock</c>: <see cref="Db.Lock(string, CancellationToken)" /> takes it for a
///     session, and the jobs battery for a transaction.
/// </summary>
internal static class SqlServerAppLock
{
    /// <summary>The longest resource name <c>sp_getapplock</c> takes.</summary>
    internal const int MaxNameLength = 255;

    /// <summary>What <c>sp_getapplock</c> answers when the lock was not granted within its timeout.</summary>
    internal const int TimedOut = -1;

    /// <summary>
    ///     The exclusive lock on <c>@name</c> for the session, waiting <c>@wait</c> milliseconds (-1 without end); answers
    ///     what <c>sp_getapplock</c> did: zero or more granted, below zero not.
    /// </summary>
    internal const string TakeForSession =
        Take + "@name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @wait; SELECT @granted;";

    /// <summary>
    ///     The exclusive lock on the resource EF fills in for <c>{0}</c>, for the transaction in progress — failing the
    ///     statement when it is not granted.
    /// </summary>
    internal const string TakeForTransaction =
        Take + "{0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'; "
        + "IF @granted < 0 THROW 51000, 'The application lock was not granted.', 1;";

    /// <summary>Lets go of the session's lock on <c>@name</c>.</summary>
    internal const string ReleaseForSession = "EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session';";

    private const string Take = "DECLARE @granted int; EXEC @granted = sp_getapplock @Resource = ";
}
