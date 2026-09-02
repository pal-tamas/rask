using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Browser;

/// <summary>
///     The seam between the C# wrappers and the browser layer they call through.
/// </summary>
/// <remarks>
///     <para>
///         A wrapper reaches the browser by handing <c>IJSRuntime</c> a dotted identifier —
///         <c>"__raskApi.geolocation"</c> — which the client dispatcher resolves against
///         <c>window</c> at call time. Nothing in the path type-checks that pairing: not Roslyn, which
///         sees a string; not tsgo, which sees an object literal; not esbuild, which bundles both
///         happily. A key renamed on either side fails at RUN time, in a browser, as
///         "Could not find '…' on target".
///     </para>
///     <para>
///         This became worth pinning when the implementations moved out of <c>rask-api.ts</c> into
///         modules under <c>Resources/browser/</c> and the registration moved with them. Moving a
///         literal across files is exactly the edit that drops one.
///     </para>
/// </remarks>
public class GlobalsContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    /// <summary>
    ///     Every <c>__raskX.member</c> a C# source names, for the namespaces <c>globals.ts</c> owns.
    /// </summary>
    /// <remarks>
    ///     The trailing <c>(?![A-Za-z*])</c> keeps prose out: doc comments say
    ///     <c>__raskApi.cookie*</c> when they mean the family, and a star is not a member.
    /// </remarks>
    public static TheoryData<string, string> CalledIdentifiers()
    {
        // The namespaces globals.ts owns, read from globals.ts itself, so moving another API into it
        // extends this gate without anyone remembering to widen a hard-coded alternation.
        var owned = OwnedNamespaces();
        Assert.NotEmpty(owned);

        var pattern = new Regex(
            @"(?<ns>__rask[A-Za-z]+)\.(?<member>[A-Za-z]+)(?![A-Za-z*])");
        var data = new TheoryData<string, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(_repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                var ns = match.Groups["ns"].Value;
                if (!owned.Contains(ns))
                {
                    // Still defined in rask-api.ts / rask-pwa.ts / rask-wasm-api.ts. Those move over
                    // API by API; each one joins this gate the moment globals.ts registers it.
                    continue;
                }

                var member = match.Groups["member"].Value;
                if (seen.Add(ns + "." + member))
                {
                    data.Add(ns, member);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CalledIdentifiers))]
    public void Every_identifier_the_C_sharp_wrappers_call_is_registered(string ns, string member)
    {
        var globals = File.ReadAllText(GlobalsPath);

        Assert.Contains(ns + " = ", globals, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(globals, @"(?m)^\s+" + Regex.Escape(member) + @"\s*:"),
            $"C# calls '{ns}.{member}', but globals.ts registers no such key — the call would fail in "
            + "the browser with \"Could not find\", and nothing before run time would say so.");
    }

    [Fact]
    public void The_call_sites_are_actually_being_scanned()
    {
        // Guards the guard. The theory above passes vacuously if the scan finds nothing — a wrong
        // root, a moved directory, a regex that stops matching — and a green vacuous gate is worse
        // than no gate. The count is deliberately loose: it asserts the scan works, not the surface.
        Assert.True(
            CalledIdentifiers().Count >= 15,
            "Found almost no __rask identifiers in src/**/*.cs, so this file is checking nothing.");
    }

    /// <summary>Every <c>window.__raskX</c> namespace <c>globals.ts</c> registers.</summary>
    private static HashSet<string> OwnedNamespaces()
    {
        var globals = File.ReadAllText(GlobalsPath);
        return Regex.Matches(globals, @"window\.(?<ns>__rask[A-Za-z]+)\s*=")
            .Select(m => m.Groups["ns"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GlobalsPath =>
        Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", "browser", "globals.ts");

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
