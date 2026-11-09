namespace Rask.Testing.Playwright.Tests;

public sealed class WalkExceptionTests
{
    [Fact]
    public void A_failed_walk_names_the_page_who_walked_it_each_problem_and_what_the_app_said_last()
    {
        string[] problems = ["GET /orders answered 404 Not Found", "console error: boom"];

        var failed = new WalkException("/orders", "ann@example.com", problems, "starting\nwarn: Rask.Live[3] a handler threw");

        Assert.Equal("/orders", failed.Path);
        Assert.Equal(problems, failed.Problems);
        Assert.Contains("Walking /orders as ann@example.com found 2 problems:", failed.Message, StringComparison.Ordinal);
        Assert.Contains("  - console error: boom", failed.Message, StringComparison.Ordinal);
        Assert.Contains("a handler threw", failed.Message, StringComparison.Ordinal);
    }
}
