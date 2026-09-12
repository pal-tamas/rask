using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     A meta-framework app VS Code's F5 launched runs the framework's own dev server under its supervisor —
///     the server <c>rask dev</c> would otherwise have started beside it.
/// </summary>
public sealed class EditorDevServerTests
{
    [Fact]
    public void An_editor_session_runs_the_dev_server_on_the_frameworks_port()
    {
        var options = new MetaHostingOptions { Framework = MetaFramework.SvelteKit, Port = 8123 };

        RaskMetaServiceCollectionExtensions.ApplyEditorDevServer(options, editorSession: true);

        Assert.True(options.RunDevServer);
        Assert.True(options.SuperviseNode);
        Assert.Equal(5173, options.Port);
    }

    [Fact]
    public void Any_other_launch_runs_the_built_entry_as_before()
    {
        var options = new MetaHostingOptions { Port = 8123 };

        RaskMetaServiceCollectionExtensions.ApplyEditorDevServer(options, editorSession: false);

        Assert.False(options.RunDevServer);
        Assert.Equal(8123, options.Port);
    }

    [Fact]
    public void A_dev_server_rask_dev_already_started_wins()
    {
        // rask dev hands the host its dev server's address, which turns supervision off. An editor session
        // must not then start a second one fighting for the same port.
        var options = new MetaHostingOptions();
        RaskMetaServiceCollectionExtensions.ApplyDevServer(options, _ => "http://localhost:3000");

        RaskMetaServiceCollectionExtensions.ApplyEditorDevServer(options, editorSession: true);

        Assert.False(options.RunDevServer);
        Assert.False(options.SuperviseNode);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData(null, "Development", true)]
    [InlineData("development", null, true)]
    [InlineData("Production", "Development", false)] // the web host's own precedence: ASPNETCORE_ wins
    [InlineData(null, null, false)]
    public void Development_is_read_from_the_environment_the_host_will_use(string? aspnet, string? dotnet, bool expected)
    {
        Assert.Equal(
            expected,
            RaskMetaServiceCollectionExtensions.IsDevelopment(name => name switch
            {
                "ASPNETCORE_ENVIRONMENT" => aspnet,
                "DOTNET_ENVIRONMENT" => dotnet,
                _ => null,
            }));
    }

    [Theory]
    [InlineData("nuxt")]
    [InlineData("nextjs")]
    [InlineData("sveltekit")]
    [InlineData("tanstack-start")]
    [InlineData("solidstart")]
    [InlineData("analog")]
    public void The_dev_port_is_the_one_rask_new_prints(string name)
    {
        // Two tables — the CLI's scaffold table and this package's presets — that have to agree, read from
        // the CLI source rather than restated, because two literals that are supposed to match are exactly
        // what drifts. The browser would open a port nothing listens on, with nothing failing.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Cli", "Scaffolding", "MetaTemplate.cs"));
        var start = source.IndexOf($"\n        \"{name}\", ", StringComparison.Ordinal);
        Assert.True(start > 0, $"MetaTemplate.cs no longer declares '{name}'");

        var url = Regex.Match(source[start..], @"DevServerUrl = ""http://localhost:(\d+)""");
        Assert.True(url.Success, $"MetaTemplate.cs has no DevServerUrl for '{name}'");

        Assert.Equal(int.Parse(url.Groups[1].Value, CultureInfo.InvariantCulture), MetaFramework.ByName(name)!.DevServerPort);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
