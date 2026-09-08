using System.Text.RegularExpressions;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     The front-end directory has two defaults — one in MSBuild, one in C# — and they have to agree.
/// </summary>
/// <remarks>
///     <para>
///         They did not. <c>RaskMetaAppDir</c> defaulted to <c>client</c> while
///         <see cref="MetaHostingOptions.AppDirectory" /> defaulted to <c>Client</c>. The build normally
///         hides the disagreement by writing the MSBuild value into assembly metadata and reading it back,
///         so only a hand-written host reaches the C# default — and there it failed in the least useful
///         way available, because a case-insensitive filesystem resolves <c>Client</c> against a
///         <c>client</c> directory without complaint. Develop on macOS, break in the container.
///     </para>
///     <para>
///         Asserted against the props file on disk rather than against a copy of the string, because two
///         literals that are supposed to be equal are exactly what went wrong: a test naming
///         <c>"client"</c> twice would have passed throughout. (#994)
///     </para>
/// </remarks>
public sealed class AppDirectoryDefaultTests
{
    [Fact]
    public void The_msbuild_default_and_the_runtime_default_are_the_same_directory()
    {
        Assert.Equal(MsBuildDefault(), new MetaHostingOptions().AppDirectory);
    }

    [Fact]
    public void The_default_survives_a_case_sensitive_filesystem()
    {
        // The whole failure was a capital that only Linux notices. Stated as its own assertion so the
        // reason this is lower case does not depend on someone reading the comment above.
        var appDir = new MetaHostingOptions().AppDirectory;

        Assert.Equal(appDir.ToLowerInvariant(), appDir);
    }

    /// <summary>The value <c>RaskMetaAppDir</c> falls back to, read from the shipped props file.</summary>
    private static string MsBuildDefault()
    {
        var props = Path.Combine(RepoRoot(), "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.props");

        Assert.True(File.Exists(props), $"the props file this asserts against is not at {props}");

        var match = Regex.Match(
            File.ReadAllText(props),
            @"<RaskMetaAppDir[^>]*>([^<]+)</RaskMetaAppDir>");

        Assert.True(match.Success, "Rask.Meta.Hosting.props no longer declares a RaskMetaAppDir default");

        return match.Groups[1].Value.Trim();
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
