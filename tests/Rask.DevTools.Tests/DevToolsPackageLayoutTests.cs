using System.Xml.Linq;

namespace Rask.DevTools.Tests;

/// <summary>
///     Where the gate's targets land inside the package.
/// </summary>
/// <remarks>
///     <para>
///         Every in-repo build imports <c>Rask.DevTools.targets</c> straight off disk through
///         <c>Directory.Build.targets</c>, so no build, test or fixture publish ever looks inside the
///         <c>.nupkg</c>. The first cut packed <c>PackagePath="build\;buildTransitive\"</c>, which writes
///         <c>build//Rask.DevTools.targets</c>: a doubled separator NuGet does not match against the package
///         id. Pack failed NU5129 — visible only to the CLI build gate, which is run by hand — and a consumer's
///         restore would not have imported the gate at all, so a scaffolded app would have published its
///         devtools in Release.
///     </para>
///     <para>
///         Read from the project file rather than from a packed package, because this runs in the unit gate
///         and a pack does not fit in it.
///     </para>
/// </remarks>
public sealed class DevToolsPackageLayoutTests
{
    private const string TargetsFile = @"buildTransitive\Rask.DevTools.targets";

    [Fact]
    public void The_gate_is_packed_under_both_folders_by_its_full_file_name()
    {
        var destinations = (PackItem().Attribute("PackagePath")?.Value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["build/Rask.DevTools.targets", "buildTransitive/Rask.DevTools.targets"], destinations);
    }

    [Fact]
    public void No_destination_names_only_a_folder()
    {
        // A trailing separator is what doubled it: NuGet appends "/<file name>" to a folder path.
        var destinations = PackItem().Attribute("PackagePath")?.Value.Split(';') ?? [];

        Assert.All(destinations, path => Assert.False(
            path.TrimEnd().EndsWith('\\') || path.TrimEnd().EndsWith('/'),
            $"PackagePath entry '{path}' names a folder; NuGet would pack it as '<folder>//Rask.DevTools.targets'."));
    }

    [Fact]
    public void The_packed_file_exists()
    {
        var file = Path.Combine(ProjectDirectory(), "buildTransitive", "Rask.DevTools.targets");

        Assert.True(File.Exists(file), $"{file} is missing — the package would ship no gate.");
    }

    private static XElement PackItem()
    {
        var project = XDocument.Load(Path.Combine(ProjectDirectory(), "Rask.DevTools.csproj"));
        var item = project.Descendants("None")
            .SingleOrDefault(n => n.Attribute("Include")?.Value == TargetsFile);

        Assert.True(item is not null, $"Rask.DevTools.csproj no longer packs {TargetsFile}.");
        Assert.Equal("true", item!.Attribute("Pack")?.Value);
        return item;
    }

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, $"no Rask.slnx above {AppContext.BaseDirectory}");
        return Path.Combine(directory!.FullName, "src", "Rask.DevTools");
    }
}
