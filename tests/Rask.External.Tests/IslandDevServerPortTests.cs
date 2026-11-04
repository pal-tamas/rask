using static Rask.External.Tests.IslandBuild;

namespace Rask.External.Tests;

/// <summary>
///     The port a project sets for its island dev server is the one the dev-session build serves from.
/// </summary>
/// <remarks>
///     Evaluated by real MSBuild over the package's shipped build assets, imported the way the SDK imports a
///     package's: the <c>.props</c> before the project body, the <c>.targets</c> after it. The URL used to be
///     defaulted in the <c>.props</c>, where the project's own <c>RaskExternalDevServerPort</c> is not set yet,
///     so the build baked 5174 into the manifest and the pointer while <c>rask dev</c>, reading the csproj, told
///     the app the dev server was on the port the project named (#1197).
/// </remarks>
public sealed class IslandDevServerPortTests
{
    [Fact]
    public async Task A_port_set_in_the_project_body_is_the_port_the_dev_server_url_carries()
    {
        var body = "<PropertyGroup><RaskExternalDevServerPort>6123</RaskExternalDevServerPort></PropertyGroup>";

        var url = await DevServerUrl(body);

        Assert.Equal("http://localhost:6123", url);
    }

    [Fact]
    public async Task A_project_that_sets_no_port_is_served_from_5174()
    {
        var body = string.Empty;

        var url = await DevServerUrl(body);

        Assert.Equal("http://localhost:5174", url);
    }

    [Fact]
    public async Task A_url_set_in_the_project_body_wins_over_the_port()
    {
        var body =
            "<PropertyGroup><RaskExternalDevServerPort>6123</RaskExternalDevServerPort>"
            + "<RaskExternalDevServerUrl>http://127.0.0.1:7000</RaskExternalDevServerUrl></PropertyGroup>";

        var url = await DevServerUrl(body);

        Assert.Equal("http://127.0.0.1:7000", url);
    }

    /// <summary>The evaluated <c>RaskExternalDevServerUrl</c> of a project with <paramref name="body" /> between the imports.</summary>
    private static async Task<string> DevServerUrl(string body)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.External", "build");
        var temp = Path.Combine(Path.GetTempPath(), "rask-island-dev-port", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var project = Path.Combine(temp, "probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(build, "Rask.External.props")}"/>
                   {body}
                   <Import Project="{Path.Combine(build, "Rask.External.targets")}"/>
                 </Project>
                 """);

            var (exit, output) = await Run(
                "dotnet", $"msbuild \"{project}\" -getProperty:RaskExternalDevServerUrl -p:RaskDevSession=true -nologo", temp);

            Assert.True(exit == 0, $"evaluating the probe failed.\n{output}");
            return output.Trim();
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }
}
