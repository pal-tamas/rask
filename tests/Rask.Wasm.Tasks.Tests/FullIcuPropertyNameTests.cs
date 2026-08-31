namespace Rask.Wasm.Tasks.Tests;

/// <summary>
///     The property Rask sets to ship full ICU must be the one the WebAssembly SDK actually reads.
/// </summary>
/// <remarks>
///     <para>
///         It was not, for as long as the feature existed (#853). <c>Rask.Wasm.targets</c> set
///         <c>WasmIncludeFullIcu</c>; the SDK reads <c>WasmIncludeFullIcuData</c>. MSBuild has no
///         unknown-property diagnostic — setting a name nothing consumes is silent and legal — so the
///         build stayed green, the publish quietly shipped the SDK's three ICU shards instead of
///         <c>icudt.dat</c>, and the trap the property was added to close stayed open.
///     </para>
///     <para>
///         It was also invisible to the obvious test. The runtime picks a shard from
///         <c>applicationCulture</c> or, failing that, <c>navigator.languages[0]</c> — so anyone
///         checking Hungarian formatting in a browser already set to Hungarian got
///         <c>icudt_no_CJK.dat</c> and correct output. Only a visitor whose browser language differed
///         from the app's saw English dates.
///     </para>
///     <para>
///         Hence a test that reads the SDK's own targets rather than restating the name. A constant
///         here would be a second place for the same typo; the SDK is the authority, so it is what gets
///         asked. If a future SDK renames the property, this fails with both names in the message
///         instead of the feature going quiet again.
///     </para>
/// </remarks>
public sealed class FullIcuPropertyNameTests
{
    [SkippableFact]
    public void Rask_sets_the_property_the_WebAssembly_SDK_reads()
    {
        var sdkTargets = WebAssemblySdkTargets();
        Skip.If(sdkTargets is null, "the Microsoft.NET.Runtime.WebAssembly.Sdk pack is not on this machine.");

        // Every WasmIncludeFullIcu* spelling the SDK knows about, comments INCLUDED: the SDK declares
        // this property in a documentation block, so stripping comments there finds nothing at all.
        var sdkNames = new HashSet<string>(
            sdkTargets!.SelectMany(file => NamesIn(File.ReadAllText(file))));

        Assert.True(
            sdkNames.Count > 0,
            $"no WasmIncludeFullIcu* property found in {string.Join(", ", sdkTargets)} — "
            + "has the SDK renamed it?");

        // Rask's side is the opposite: comments STRIPPED, because this file explains the old
        // misspelling by name and prose is not a property set.
        var raskNames = NamesIn(StripComments(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Rask.Wasm", "build", "Rask.Wasm.targets"))));

        Assert.True(
            raskNames.Count > 0,
            "Rask.Wasm.targets no longer sets a WasmIncludeFullIcu* property at all.");

        foreach (var name in raskNames)
        {
            Assert.True(
                sdkNames.Contains(name),
                $"Rask.Wasm.targets sets '{name}', which the WebAssembly SDK does not read. "
                + $"It reads: {string.Join(", ", sdkNames)}. A property MSBuild does not recognise is "
                + "set silently and consumed by nobody, so the publish would ship the SDK's ICU shards "
                + "and an app whose visitor's browser language differs from its own would format dates "
                + "in the wrong language, with every check green. This is #853.");
        }
    }

    private static HashSet<string> NamesIn(string targets) =>
        [.. System.Text.RegularExpressions.Regex
            .Matches(targets, @"WasmIncludeFullIcu[A-Za-z]*")
            .Select(m => m.Value)];

    private static string StripComments(string xml) =>
        System.Text.RegularExpressions.Regex.Replace(
            xml, @"<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>The SDK targets that document and consume the property, from the installed pack.</summary>
    private static IReadOnlyList<string>? WebAssemblySdkTargets()
    {
        var packs = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!,
            "..", "..", "packs", "Microsoft.NET.Runtime.WebAssembly.Sdk");

        if (!Directory.Exists(packs))
        {
            return null;
        }

        // Newest pack on the box, ordered as VERSIONS. Ordinal string ordering puts 10.0.8 above
        // 10.0.11, which read an older pack than the one this machine builds with — caught here only
        // because the two happened to differ.
        var newest = Directory.EnumerateDirectories(packs)
            .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
            .Where(p => p.Version is not null)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Dir)
            .FirstOrDefault();

        if (newest is null)
        {
            return null;
        }

        // Both files: the property is documented in one and consumed in the other, and which is which
        // is the SDK's business rather than something worth pinning.
        var found = new[] { "WasmApp.Common.targets", "BrowserWasmApp.targets" }
            .Select(name => Path.Combine(newest, "Sdk", name))
            .Where(File.Exists)
            .ToArray();

        return found.Length > 0 ? found : null;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
