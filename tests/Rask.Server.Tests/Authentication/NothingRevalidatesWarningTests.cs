using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     An app that signs users in and registers nothing a live page can ask about them is told so as it starts: its
///     open pages cannot learn that a sign-in ended.
/// </summary>
[Collection("DiagnosticsSink")]
public sealed class NothingRevalidatesWarningTests
{
    [Fact]
    public void A_host_with_sign_in_and_no_revalidator_is_warned_once_and_told_what_to_register()
    {
        var said = Starting(static services => services.AddAuthentication("TestCookie").AddCookie("TestCookie"));

        var warning = Assert.Single(said);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Rask.Live", warning.Category);
        Assert.Contains("docs/authentication.md#what-ties-a-live-page-to-its-user", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_registers_a_revalidator_is_told_nothing()
    {
        var said = Starting(static services =>
        {
            services.AddAuthentication("TestCookie").AddCookie("TestCookie");
            services.AddSingleton<ISessionRevalidator, Anybody>();
        });

        Assert.Empty(said);
    }

    [Fact]
    public void A_host_with_no_sign_in_is_told_nothing()
    {
        var said = Starting(static _ => { });

        Assert.Empty(said);
    }

    // What the host's own log was told about revalidation as it started.
    private static CapturedLog.Entry[] Starting(Action<IServiceCollection> configure)
    {
        var log = new CapturedLog();

        using var host = RaskTestHost.Create<SignInTestApp>(services =>
        {
            services.AddLogging(logging => logging.AddProvider(log));
            configure(services);
        });

        return [.. log.Entries.Where(static e => e.Message.Contains("ISessionRevalidator", StringComparison.Ordinal))];
    }

    private sealed class Anybody : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(principal);
    }
}
