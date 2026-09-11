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

    /// <summary>
    ///     The range the templates actually ship, read from the committed manifests.
    /// </summary>
    /// <remarks>
    ///     It used to be read back out of the package.json PATCH the generator produced, because the
    ///     manifest itself belonged to create-vite and existed only after a scaffold had run. The
    ///     manifests are committed now, so this reads the bytes a scaffolded app receives — and reads
    ///     EVERY template rather than React alone, which is what makes a range that drifts in one
    ///     framework's manifest a failure here instead of a surprise for whoever picks that template.
    /// </remarks>
    private static string SpaTailwindRange
    {
        get
        {
            var found = TemplateManifests("tailwindcss");

            Assert.True(
                found.Count > 0,
                "No committed template manifest declares tailwindcss. Either the templates stopped "
                + "shipping Tailwind or this pin is looking in the wrong place.");

            var distinct = found.Values.Distinct(StringComparer.Ordinal).ToArray();
            Assert.True(
                distinct.Length == 1,
                "The templates do not agree on a Tailwind range, so two scaffolded apps would compile "
                + "the same classes with different compilers:\n  "
                + string.Join("\n  ", found.Select(f => $"{f.Key}: {f.Value}")));

            return distinct[0];
        }
    }

    /// <summary>Every committed client manifest's range for <paramref name="package"/>, by template.</summary>
    internal static Dictionary<string, string> TemplateManifests(string package)
    {
        var root = Path.Combine(RepoRoot(), "src", "Rask.Templates");
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var manifest in Directory.EnumerateFiles(root, "package.json", SearchOption.AllDirectories))
        {
            var match = Regex.Match(
                File.ReadAllText(manifest), $@"""{Regex.Escape(package)}""\s*:\s*""([^""]+)""");

            if (match.Success)
            {
                found[Path.GetRelativePath(root, manifest).Replace(Path.DirectorySeparatorChar, '/')] =
                    match.Groups[1].Value;
            }
        }

        return found;
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
