namespace Rask.Blazor.Tests;

/// <summary>
///     Pins the package's shape: both hosts, and opt-in on both.
/// </summary>
/// <remarks>
///     <para>
///         A hosted component renders to markup in process, which browser-WebAssembly does as readily
///         as a server — so both frameworks are supported and share one code path with no <c>#if</c>.
///         The difference is trimming, and that is the consuming app's publish-time choice rather
///         than something this package can decide.
///     </para>
///     <para>
///         What must not drift is that it stays OUT of the <c>Rask</c> meta-package. Everything there
///         is referenced by every app on that framework, and an app that wants nothing to do with
///         Blazor should not carry its renderer.
///     </para>
///     <para>
///         The csproj used to CLAIM this test existed while it did not, which is worse than having
///         neither: a comment asserting a guard is exactly what stops the next reader from checking.
///     </para>
/// </remarks>
public sealed class PackageShapeTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Rask.slnx not found above the test binary.");
    }

    [Fact]
    public void Rask_Blazor_builds_for_both_hosts()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Blazor", "Rask.Blazor.csproj"));

        Assert.Contains("<TargetFrameworks>net10.0;net10.0-browser</TargetFrameworks>", csproj, StringComparison.Ordinal);

        // The browser target has no shared framework, so the renderer has to come from the package.
        // Losing this reference does not fail the server build — only the browser one, which is the
        // half nobody runs locally.
        Assert.Contains("Microsoft.AspNetCore.Components.Web", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_frameworks_have_a_public_API_baseline()
    {
        // A missing baseline is a build error, but only for the framework that is missing it — so a
        // browser-only gap would surface on someone else's machine rather than here.
        var api = Path.Combine(RepoRoot(), "src", "Rask.Blazor", "PublicAPI");

        Assert.True(File.Exists(Path.Combine(api, "net10.0", "PublicAPI.Unshipped.txt")));
        Assert.True(File.Exists(Path.Combine(api, "net10.0-browser", "PublicAPI.Unshipped.txt")));
    }

    [Fact]
    public void The_Rask_meta_package_references_Rask_Blazor_from_neither_framework()
    {
        // Referencing it from the net10.0 group would pull the Blazor renderer into every server app
        // that wants nothing to do with it; from the browser group it would not restore at all.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask", "Rask.csproj"));

        Assert.DoesNotContain("Rask.Blazor", csproj, StringComparison.Ordinal);
    }
}
