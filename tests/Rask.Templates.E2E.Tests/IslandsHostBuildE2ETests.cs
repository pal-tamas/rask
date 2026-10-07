namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A template scaffolded with an island runtime compiles, and its own first test passes.
/// </summary>
/// <remarks>
///     A class of its own so xUnit runs it beside <see cref="TemplateBuildE2ETests"/> rather than after it:
///     the cases are independent builds, and one class is one collection, which is one core.
/// </remarks>
public sealed class IslandsHostBuildE2ETests
{
    [Theory]
    [InlineData("server", "react")]
    [InlineData("server", "blazor")]
    [InlineData("wasm", "lit")]
    [InlineData("wasm", "blazor")]
    public async Task An_islands_host_compiles(string template, string runtime)
    {
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = $"Isl{template}{runtime}";
        var work = TemplateBuildE2ETests.NewWorkingDirectory();

        try
        {
            var projectDirectory = Path.Combine(work, name);
            var result = TemplateBuildE2ETests.Scaffold(template, projectDirectory, name, version, [runtime]);
            TemplateBuildE2ETests.Write(result, projectDirectory, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -warnaserror -m:1 "
                + "-p:RaskSpaBuild=false -p:RaskExternalBuild=false");

            Assert.True(
                exit == 0,
                $"--template {template} --islands {runtime} does not compile:\n"
                + CliBuildE2E.Diagnostics(output));

            // The home page renders the island, so the scaffold's own first test renders one too.
            var (tested, testOutput) = await CliBuildE2E.RunDotnet(
                $"test \"{Path.Combine(projectDirectory, name + ".Tests", name + ".Tests.csproj")}\" -warnaserror -m:1 "
                + "-p:RaskSpaBuild=false -p:RaskExternalBuild=false");

            Assert.True(
                tested == 0,
                $"--template {template} --islands {runtime}: the scaffolded tests do not pass:\n"
                + CliBuildE2E.Diagnostics(testOutput));
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }
}
