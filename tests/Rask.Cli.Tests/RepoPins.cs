using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     Reads this repo's own version pins out of the files that declare them, for the tests that assert
///     a second copy of a number has not drifted from the first.
/// </summary>
/// <remarks>
///     <para>
///         One reader, deliberately. The pins were previously parsed by a regex in <c>CliBuildE2E</c> that
///         required the exact self-closing single-space form — <c>&lt;PackageVersion Include="x"
///         Version="y"/&gt;</c>. A reformat of <c>Directory.Packages.props</c> that split an attribute onto
///         its own line, or wrote <c>&lt;/PackageVersion&gt;</c> instead of <c>/&gt;</c>, would have matched
///         nothing and silently dropped that package — surfacing much later as
///         <c>CliBuildE2E</c>'s "No version known" throw, which reads like a missing pin rather than a
///         parser that stopped seeing it. <see cref="XDocument" /> does not care how the XML is laid out.
///     </para>
///     <para>
///         The same reasoning already governs the TypeScript toolchain pin, which
///         <c>ResolveTypeScriptToolTaskTests</c> loads out of <c>Rask.Core.targets</c> rather than
///         restating: a pin stated anywhere else is a second copy that can drift.
///     </para>
/// </remarks>
internal static class RepoPins
{
    /// <summary>Every <c>PackageVersion</c> in <c>Directory.Packages.props</c>, by package id.</summary>
    public static Dictionary<string, string> Packages()
    {
        var path = Path.Combine(CliBuildE2E.FindRepoRoot(), "Directory.Packages.props");
        var pins = XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => (Id: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version")))
            .Where(p => p.Id is not null && p.Version is not null)
            .ToDictionary(p => p.Id!, p => p.Version!, StringComparer.Ordinal);

        Assert.True(
            pins.Count > 0,
            $"No <PackageVersion> elements were read out of {path}. The file moved, or central package "
            + "management was turned off — either way every pin-agreement test below would pass by "
            + "checking nothing.");

        return pins;
    }

    /// <summary>The version pinned for <paramref name="package" />, or a failure naming the file.</summary>
    public static string Package(string package)
    {
        var pins = Packages();
        Assert.True(
            pins.TryGetValue(package, out var version),
            $"'{package}' is no longer pinned in Directory.Packages.props.");

        return version!;
    }

    /// <summary>The whole text of a repo file, for the pins that live in shell, docs or C# rather than XML.</summary>
    public static string Text(string relativePath) =>
        File.ReadAllText(Path.Combine(
            CliBuildE2E.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
