using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The production error page a scaffolded app gets for exceptions thrown <b>outside</b> a component tree.
/// (Inside one, <c>ErrorBoundary</c> already handles it.)
/// </summary>
public sealed class ErrorPageScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Theory]
    [InlineData]
    [InlineData("auth")]
    [InlineData("data", "jobs", "ops", "push", "logs")]
    public void Every_server_app_gets_an_error_page(params string[] flags)
    {
        // Not a battery and not opt-in: without it an unhandled exception is a bare 500 with an empty body.
        var files = Generate(flags);

        Assert.Contains("Features/Shared/ErrorPage.cs", files.Keys);
        Assert.Contains("""[Route("/error")]""", files["Features/Shared/ErrorPage.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_shows_a_correlation_id_and_nothing_about_the_exception()
    {
        // The whole point. This page is served to whoever hit the error, so leaking the message or a stack
        // trace is an information disclosure — a prettier empty 500 would be no improvement, but a page
        // that renders `ex.Message` is actively worse than the blank one it replaces. The detail goes to
        // ILogger; the id is what ties the two together.
        var page = Generate()["Features/Shared/ErrorPage.cs"];

        Assert.Contains("Activity.Current?.Id", page, StringComparison.Ordinal);

        foreach (var leak in new[] { "IExceptionHandlerFeature", "StackTrace", ".Message", "exception.ToString" })
        {
            Assert.DoesNotContain(leak, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_page_is_reachable_without_signing_in()
    {
        // An error page that redirects to /login is worse than the error. The attribute is emitted whether
        // or not the app has accounts, so adding a fallback authorization policy later can't lock it away.
        Assert.Contains("[AllowAnonymous]", Generate("data")["Features/Shared/ErrorPage.cs"], StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymous]", Generate()["Features/Shared/ErrorPage.cs"], StringComparison.Ordinal);
    }
}
