using Rask.TestSupport;

namespace Rask.External.Tests;

// The host element is a wire between two languages: C# writes the attributes, rask-external.js reads
// them. Nothing about that agreement is checked by either compiler — rename one side and the island
// simply stops mounting, with no build failure and no error until someone opens the page.
//
// So the agreement is pinned here, against the shipped JavaScript rather than a copy of it.
public sealed class ExternalWireContractTests
{
    private static string Runtime() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.External", "wwwroot", "rask-external.js"));

    [Theory]
    [InlineData(ExternalDefaults.NameAttribute)]
    [InlineData(ExternalDefaults.PropsAttribute)]
    [InlineData(ExternalDefaults.HydrateAttribute)]
    public void The_client_runtime_reads_every_attribute_the_server_writes(string attribute)
    {
        // getAttribute("name"), getAttribute("props"), getAttribute("hydrate") — the three the client
        // acts on. `module` and `runtime` are written for tooling and devtools rather than read by the
        // runtime, which resolves through the build's manifest instead.
        Assert.Contains($"getAttribute(\"{attribute}\")", Runtime(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_client_runtime_watches_the_props_attribute_for_changes()
    {
        // The single op a re-render produces for an island. If the observer filtered on a different
        // name, props would reach the browser on first paint and never update again — the island would
        // look like it worked.
        Assert.Contains($"attributeFilter: [\"{ExternalDefaults.PropsAttribute}\"]", Runtime(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_client_runtime_matches_the_host_tag_the_server_renders()
    {
        Assert.Contains(
            $"HOST_TAG = \"{ExternalDefaults.HostTag.ToUpperInvariant()}\"",
            Runtime(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_script_url_points_at_a_file_this_package_actually_ships()
    {
        // The generated HeadAssets <script src> is this constant. A wrong path is a 404 that leaves
        // every island on the page unmounted, and static web assets make the URL easy to drift from
        // the file's real location.
        var relative = ExternalDefaults.RuntimeScriptUrl["/_content/Rask.External/".Length..];
        var shipped = Path.Combine(RepoRoot(), "src", "Rask.External", "wwwroot", relative);

        Assert.True(File.Exists(shipped), $"{ExternalDefaults.RuntimeScriptUrl} resolves to {shipped}, which is not there");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate Rask.slnx from {AppContext.BaseDirectory}");
    }
}
