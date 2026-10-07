using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Who may dispatch a message to a scaffolded front-end app's host. The starter's greeting is public by
///     its own handlers; everything a developer adds later fails closed once there are accounts to require.
/// </summary>
public sealed class SpaDispatchAuthorizationTests
{
    private const string Root = "/tmp/app";

    public static TheoryData<string> Templates() => [.. SpaFramework.All.Select(framework => framework.Key)];

    [Theory]
    [MemberData(nameof(Templates))]
    public void With_a_database_only_the_starter_greeting_answers_an_anonymous_caller(string template)
    {
        var files = Generate(template, new ServerBatteries { Data = true });

        Assert.DoesNotContain("RequireAuthenticatedUser", files["appsettings.json"], StringComparison.Ordinal);
        Assert.Equal(2, Count(files["Features/Hello/HelloHandlers.cs"], "[AllowAnonymous]\npublic sealed class"));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Without_a_database_there_are_no_accounts_so_dispatch_stays_open(string template)
    {
        var files = Generate(template, new ServerBatteries());

        Assert.Contains("\"RequireAuthenticatedUser\": false", files["appsettings.json"], StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Generate(string template, ServerBatteries batteries) =>
        ProjectGenerator
            .GenerateSpa(Root, "Shop", SpaFramework.All.Single(f => f.Key == template), batteries, "1.2.3")
            .Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content.ReplaceLineEndings("\n"));

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
