using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Cli.Tests;

/// <summary>
///     A front-end template installs the Tailwind the C# hosts compile with.
/// </summary>
/// <remarks>
///     Tailwind is pinned twice, for the two paths that install it: <c>RaskTailwindVersion</c> picks the
///     standalone binary a C# host downloads, and a front-end template puts a <c>tailwindcss</c> range in its
///     client's <c>package.json</c>. The npm <b>range must accept the pinned version</b> — <c>^4.3.0</c> and
///     <c>4.3.3</c> agree, <c>^4.3.0</c> and <c>5.0.1</c> do not — or the same classes compile differently.
/// </remarks>
public sealed class TailwindVersionPinTests
{
    [Fact]
    public void The_npm_range_accepts_the_version_the_C_sharp_path_downloads()
    {
        var pinned = Version.Parse(Regex.Match(
            RepoPins.Text("src/Rask.Tailwind/build/Rask.Tailwind.props"),
            @"<RaskTailwindVersion[^>]*>([0-9]+\.[0-9]+\.[0-9]+)<").Groups[1].Value);

        var ranges = TemplateManifests("tailwindcss");

        Assert.NotEmpty(ranges);
        Assert.All(ranges, range =>
        {
            var floor = Regex.Match(range.Value, @"^\^([0-9]+\.[0-9]+\.[0-9]+)$");
            Assert.True(floor.Success, $"{range.Key}: expected a caret range like ^4.3.0, got '{range.Value}'.");
            var lowest = Version.Parse(floor.Groups[1].Value);
            Assert.True(
                pinned.Major == lowest.Major && pinned >= lowest,
                string.Create(CultureInfo.InvariantCulture, $"{range.Key} installs Tailwind '{range.Value}', which does not accept the {pinned} a C# host downloads."));
        });
    }

    /// <summary>Every committed client manifest's range for <paramref name="package"/>, by template.</summary>
    internal static Dictionary<string, string> TemplateManifests(string package)
    {
        var root = Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");
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
}
