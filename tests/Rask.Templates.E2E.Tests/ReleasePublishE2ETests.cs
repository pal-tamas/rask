namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A scaffolded server app is published in Release, the way a deploy publishes it, and started out of
///     its publish folder.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TemplateBuildE2ETests" /> builds in Debug, so nothing published a template that carries
///         Rask.DevTools and then ran it: a Release publish removes the devtools, and an app whose own assembly still named
///         them as an MVC application part died in <c>AddMvcCore</c> before it listened (#1226), with every build
///         and every publish check green.
///     </para>
/// </remarks>
public sealed class ReleasePublishE2ETests
{
    [Fact]
    public async Task A_server_scaffold_published_in_Release_starts_without_its_devtools()
    {
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        const string name = "RelServer";
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var project = Path.Combine(work, name);
        var published = Path.Combine(work, "published");

        try
        {
            TemplateBuildE2ETests.Write(
                TemplateBuildE2ETests.Scaffold("server", project, name, version), project, feed);
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{Path.Combine(project, name + ".csproj")}\" -c Release -warnaserror -m:1 "
                + $"-p:RaskSpaBuild=false -o \"{published}\"");

            Assert.True(exit == 0, $"the server template does not publish in Release:\n{CliBuildE2E.Diagnostics(output)}");
            // Throws with the host's log when it exits before answering, which is how #1226 showed itself.
            await using var app = await ScaffoldedHost.StartPublishedAsync(published, name, TimeSpan.FromMinutes(2));
            Assert.False(File.Exists(Path.Combine(published, "Rask.DevTools.dll")), "the Release publish carries Rask.DevTools.dll");
            Assert.DoesNotContain("Rask.DevTools", app.Log, StringComparison.Ordinal);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }
}
