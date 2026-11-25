using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Rask.Core;

namespace Rask.Server.Tests.App;

/// <summary>
///     An app Rask.Testing boots from its Program.cs, because a test needed it and bound no database of its own, is
///     never pointed at a database server: it is refused before anything opens it, saying what to do instead.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class TestBootDatabaseTests
{
    [Fact]
    public void An_app_booted_for_a_test_is_refused_a_server_database_before_it_connects()
    {
        var capture = AppCapture.Begin();

        var refused = Record.Exception(() => CreateApp("sqlserver").Build<MinimalApp>());

        AppCapture.End();
        Assert.False(capture.Wait(TimeSpan.Zero));
        var message = Assert.IsType<InvalidOperationException>(refused).Message;
        Assert.Contains("'sqlserver'", message, StringComparison.Ordinal);
        Assert.Contains("TestDatabase.Start", message, StringComparison.Ordinal);
        Assert.Contains("Events.Fake()", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_booted_for_a_test_keeps_its_own_sqlite_database()
    {
        AppCapture.Begin();

        var built = Record.Exception(() => CreateApp("sqlite").Build<MinimalApp>());

        AppCapture.End();
        Assert.Null(built);
    }

    [Fact]
    public void An_app_started_outside_a_test_boot_opens_the_server_it_is_configured_for()
    {
        var built = Record.Exception(() => CreateApp("sqlserver", "Server=127.0.0.1,1;Database=app").Build<MinimalApp>());

        Assert.Null(built);
    }

    private static RaskApp CreateApp(string provider, string? connectionString = null) =>
        RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Rask:Database:Provider"] = provider,
                ["Rask:ConnectionStrings:App"] = connectionString ?? "Data Source=:memory:",
            });
        });
}
