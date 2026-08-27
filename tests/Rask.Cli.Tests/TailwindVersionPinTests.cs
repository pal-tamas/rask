using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The two Tailwind version pins must agree.
/// </summary>
/// <remarks>
///     <para>
///         Tailwind is pinned twice, in two languages, for the two paths that install it: the C# host path
///         uses <c>RaskTailwindVersion</c> in <c>Rask.Tailwind.props</c> to pick which standalone binary to
///         download, and the front-end templates put a <c>tailwindcss</c> range in the client's
///         <c>package.json</c>. A comment beside the second says the two must not drift, and until now
///         nothing checked it.
///     </para>
///     <para>
///         Drift here is quiet and slow: a project scaffolded with <c>--tailwind</c> on a C# host and one
///         scaffolded on a front end would compile the same classes with different compilers, and the
///         difference shows up as a rendering discrepancy nobody thinks to blame on a version.
///     </para>
///     <para>
///         The check is that the npm <b>range accepts the pinned version</b>, not that the strings match —
///         they are deliberately different shapes. <c>^4.3.0</c> and <c>4.3.3</c> agree; <c>^4.3.0</c> and
///         <c>5.0.1</c> do not.
///     </para>
/// </remarks>
public sealed class TailwindVersionPinTests
{
    [Fact]
    public void The_npm_range_accepts_the_version_the_C_sharp_path_downloads()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Tailwind", "build", "Rask.Tailwind.props"));
        var pinned = Regex.Match(props, @"<RaskTailwindVersion[^>]*>([0-9]+)\.([0-9]+)\.([0-9]+)<").Groups;
        Assert.True(pinned.Count == 4, "Could not read RaskTailwindVersion out of Rask.Tailwind.props.");

        var range = Regex.Match(SpaTailwindRange, @"^\^([0-9]+)\.([0-9]+)\.([0-9]+)$").Groups;
        Assert.True(range.Count == 4, $"Expected a caret range like ^4.3.0, got '{SpaTailwindRange}'.");

        int PinnedPart(int i) => int.Parse(pinned[i].Value);
        int RangePart(int i) => int.Parse(range[i].Value);

        // A caret range allows anything from its floor up to the next major.
        Assert.True(
            PinnedPart(1) == RangePart(1),
            $"The C# path downloads Tailwind {PinnedPart(1)}.x while the front-end templates install "
            + $"'{SpaTailwindRange}'. Different majors compile differently; the two paths must not drift.");

        var pinnedIsAtLeastFloor =
            PinnedPart(2) > RangePart(2) || (PinnedPart(2) == RangePart(2) && PinnedPart(3) >= RangePart(3));

        Assert.True(
            pinnedIsAtLeastFloor,
            $"'{SpaTailwindRange}' does not accept {PinnedPart(1)}.{PinnedPart(2)}.{PinnedPart(3)}, the "
            + "version the C# path downloads, so a front end would install an older compiler than a C# "
            + "host. Raise the range floor with the pin.");
    }

    /// <summary>Reads the range the SPA generator writes, via the package.json patch that carries it.</summary>
    private static string SpaTailwindRange
    {
        get
        {
            var result = ProjectGenerator.GenerateSpa(
                "/proj/App", "App", SpaFramework.React, new ServerBatteries { Styling = Styling.Tailwind }, "9.9.9");

            var patch = result.Patches.Single(p => p.Path.EndsWith("package.json", StringComparison.Ordinal));
            var json = patch.Transform("""{ "dependencies": {}, "devDependencies": {}, "scripts": {} }""");

            return Regex.Match(json, @"""tailwindcss""\s*:\s*""([^""]+)""").Groups[1].Value;
        }
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

        throw new InvalidOperationException("Could not locate Rask.slnx.");
    }
}
