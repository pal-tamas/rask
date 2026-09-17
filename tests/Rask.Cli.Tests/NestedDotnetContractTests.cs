using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     A package's build targets never shell out to a bare <c>dotnet build</c> or <c>dotnet publish</c>; they
///     run <c>$(_RaskSdkDotnet)</c>, the SDK the build is already running on.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>dotnet</c> re-resolves the SDK from the <c>global.json</c> in the directory it runs in —
///         the app's — while the build that launched it resolved its SDK from wherever that build started.
///         Every scaffold writes a <c>global.json</c> now, so <c>dotnet build path/to/App.csproj</c> from any
///         other folder ran the outer build on one SDK and the nested one on another, and the child, having
///         inherited the parent's <c>MSBuildSDKsPath</c>, loaded the other SDK's targets and failed with
///         MSB4216. That shipped as a green unit suite and a red <c>wasm-hosted</c> template gate, and the
///         same bug sat unexercised in two more packages.
///     </para>
///     <para>
///         Asserted on the files rather than by building, because the failure needs two SDKs installed and a
///         specific working directory to show up at all — exactly the conditions a unit run never has.
///     </para>
/// </remarks>
public sealed partial class NestedDotnetContractTests
{
    private const string Property = "_RaskSdkDotnet";

    [GeneratedRegex(@"^\s*dotnet\s+(build|publish|pack|restore|msbuild|test|run|watch)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareSdkCommand { get; }

    [Fact]
    public void No_packed_build_file_runs_a_bare_dotnet_sdk_command()
    {
        var offenders = PackedBuildFiles()
            .SelectMany(file => XDocument.Load(file).Descendants("Exec")
                .Select(exec => (file, command: (string?)exec.Attribute("Command") ?? "")))
            .Where(e => BareSdkCommand.IsMatch(e.command))
            .Select(e => $"{Relative(e.file)}: {e.command.Split(' ', 3)[0]} {e.command.Split(' ', 3)[1]} …")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These run a bare `dotnet` SDK command, which re-resolves the SDK from the app's global.json and "
            + $"then loads the parent build's SDK paths into the wrong runtime. Use $({Property}):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_file_that_uses_the_property_defines_it_and_the_definitions_agree()
    {
        var users = PackedBuildFiles()
            .Where(file => File.ReadAllText(file).Contains($"$({Property})", StringComparison.Ordinal))
            .ToArray();

        // Guards the guard: if nothing used the property, the test below would pass over an empty set.
        Assert.True(users.Length >= 3, $"expected Rask.Server, Rask.Wasm and Rask.Spa.Hosting to use $({Property}).");

        var definitions = users.ToDictionary(
            Relative,
            file => string.Join(
                "\n",
                XDocument.Load(file).Descendants(Property)
                    .Select(p => $"{(string?)p.Attribute("Condition")} => {p.Value}")));

        var missing = definitions.Where(d => d.Value.Length == 0).Select(d => d.Key).ToArray();
        Assert.True(missing.Length == 0, $"uses $({Property}) without defining it: {string.Join(", ", missing)}");

        // Identical, not merely present. One app can import two of these packages, and whichever is
        // evaluated first wins — so two definitions that differ would make the SDK a nested build runs on
        // depend on package import order.
        var distinct = definitions.Values.Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(
            distinct.Length == 1,
            $"the $({Property}) definitions differ between files:\n"
            + string.Join("\n", definitions.Select(d => $"--- {d.Key}\n{d.Value}")));
    }

    /// <summary>The MSBuild files the host packages pack into <c>build/</c>.</summary>
    private static IEnumerable<string> PackedBuildFiles()
    {
        var src = Path.Combine(RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".targets", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal))
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        || f.Contains($"{Path.DirectorySeparatorChar}buildTransitive{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
