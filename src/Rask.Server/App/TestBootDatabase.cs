using Rask.Core;

namespace Rask;

/// <summary>
///     Keeps an app Rask.Testing boots from its Program.cs — because a test needed it and had bound no database of its
///     own — off every database server: it gets a SQLite database in a folder of its own, and never the server the app
///     is configured for.
/// </summary>
/// <remarks>
///     A test that publishes an event, sends a command or visits a page without a <c>TestDatabase</c> boots the app;
///     an app configured for PostgreSQL or SQL Server would then connect to that server — the developer's, or worse —
///     from a test that asked for nothing of the kind. It is refused before anything opens it, saying what to do.
/// </remarks>
internal static class TestBootDatabase
{
    /// <summary>Refuses a server database for an app booted by Rask.Testing; anything else is left as it is.</summary>
    /// <exception cref="InvalidOperationException">Rask.Testing is booting the app, and it is configured for a server.</exception>
    internal static void Refuse(RaskDatabaseProvider provider)
    {
        if (AppCapture.Current is null || provider == RaskDatabaseProvider.Sqlite)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Rask.Testing booted this app from its Program.cs because the test needed it (an event published, a command "
            + "sent or a page visited with no TestDatabase bound). A booted app gets a SQLite database of its own, but "
            + $"{RaskDatabase.ProviderKey} is '{RaskDatabase.Name(provider)}', so it would connect to a real server, and a "
            + "test never does unless it set one up. Start the test's own database first — `await using var database = "
            + "await TestDatabase.Start<AppDbContext>();` — record what the test publishes with `using var events = "
            + "Events.Fake();`, or set Rask__Database__Provider=sqlite for the Test environment (appsettings.Test.json).");
    }
}
