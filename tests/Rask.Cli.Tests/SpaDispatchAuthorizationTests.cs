using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Who may dispatch a message to a scaffolded front-end app's server, on every SPA and meta template.
/// </summary>
/// <remarks>
///     These used to switch the sign-in requirement off unconditionally, so the starter's greeting could answer an
///     anonymous landing page — and with it every command a developer added later, to anyone with curl. Now the
///     greeting's own handlers are public and everything else fails closed once there are accounts to require.
/// </remarks>
public sealed class SpaDispatchAuthorizationTests
{
    private const string Root = "/tmp/app";

    public static TheoryData<string> Templates()
    {
        var names = new TheoryData<string>();
        foreach (var spa in SpaFramework.All)
        {
            names.Add("spa:" + spa.Key);
        }

        foreach (var meta in MetaTemplate.All)
        {
            names.Add("meta:" + meta.Key);
        }

        return names;
    }

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

    private static Dictionary<string, string> Generate(string template, ServerBatteries batteries)
    {
        var (lane, name) = (template[..template.IndexOf(':')], template[(template.IndexOf(':') + 1)..]);
        var result = lane == "spa"
            ? ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.All.Single(f => f.Key == name), batteries, "1.2.3")
            : ProjectGenerator.GenerateMeta(Root, "Shop", MetaTemplate.All.Single(t => t.Key == name), batteries, "1.2.3");

        return result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content.ReplaceLineEndings("\n"));
    }

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
