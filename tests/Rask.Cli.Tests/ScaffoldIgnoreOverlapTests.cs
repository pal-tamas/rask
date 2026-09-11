using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Nothing the scaffolder writes may land where the scaffolder also gitignores it.
/// </summary>
/// <remarks>
///     <para>
///         <c>rask new Shop --template react --push</c> wrote its push helper to
///         <c>client/src/rask/push.ts</c> and, in the same run, appended <c>src/rask/</c> to the client's
///         <c>.gitignore</c>. Everything else in that directory is rewritten from the server assembly on
///         every build, so ignoring it is right — but <c>push.ts</c> was written once, by the CLI, and by
///         nothing afterwards. It was therefore never committed, and a fresh clone of a <c>--push</c>
///         project simply did not have it: the client failed to build on an unresolved import, in a
///         project whose <c>--push</c> flag was the only reason the file existed.
///     </para>
///     <para>
///         Asserted as the general rule rather than as "push.ts is somewhere else", because the specific
///         file is not the interesting part — the overlap is, and it can be re-introduced by any future
///         file added to a generated directory. (#957)
///     </para>
///     <para>
///         It reads the <c>.gitignore</c> FILES a scaffold writes, not only the patches it appends. The
///         patches were all it used to read, and once the templates were committed trees no scaffold had a
///         <c>.gitignore</c> patch left — so every case here passed while checking nothing. The committed
///         <c>.vscode/</c> debug setup is the case that made that visible: the scaffold ignores
///         <c>/.vscode/*</c> and re-includes exactly the four files it ships, so a matcher that did not
///         understand <c>dir/*</c> and <c>!</c> would either miss the rule or report files that are tracked.
///     </para>
/// </remarks>
public sealed class ScaffoldIgnoreOverlapTests
{
    private const string Root = "/scaffold-root";
    private const string Version = "1.0.0";

    public static TheoryData<string> Frameworks
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var framework in SpaFramework.All)
            {
                data.Add(framework.Key);
            }

            return data;
        }
    }

    public static TheoryData<string> MetaFrameworks
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var framework in MetaTemplate.All)
            {
                data.Add(framework.Key);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void No_scaffolded_file_lands_where_the_scaffold_ignores_it(string key)
    {
        Assert.True(SpaFramework.TryGet(key, out var framework));

        // --push is the battery that put a hand-owned file in a generated directory, so it is the one
        // that has to be on. Pwa comes with it (--push implies --pwa) and brings its own client files.
        AssertNoOverlap(ProjectGenerator.GenerateSpa(Root, "Shop", framework, new ServerBatteries { Push = true }, Version));
    }

    [Theory]
    [MemberData(nameof(MetaFrameworks))]
    public void No_meta_scaffold_file_lands_where_the_scaffold_ignores_it(string key)
    {
        var framework = MetaTemplate.All.Single(t => t.Key == key);

        AssertNoOverlap(ProjectGenerator.GenerateMeta(Root, "Shop", framework, new ServerBatteries { Push = true }, Version));
    }

    [Fact]
    public void No_server_scaffold_file_lands_where_the_scaffold_ignores_it()
    {
        AssertNoOverlap(ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries(), Version));
    }

    [Fact]
    public void The_push_helper_is_still_scaffolded_when_push_is_asked_for()
    {
        // Guards the cheap way to pass the test above: not writing the file at all. It has to be both
        // present AND outside the ignored directory.
        var result = ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.React, new ServerBatteries { Push = true }, Version);

        Assert.Contains(result.Files, f => Normalize(f.Path).EndsWith("push.ts", StringComparison.Ordinal));
    }

    [Fact]
    public void The_matcher_reads_the_ignore_files_a_scaffold_writes()
    {
        // The vacuous pass this class used to have, pinned shut: a scaffold whose root .gitignore ignores
        // /.vscode/* has to be SEEN to do so, or none of the cases above are checking anything.
        var result = ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries(), Version);

        var rules = IgnoreRules(result).ToList();

        Assert.Contains(rules, r => r.Ignored == Root + "/.vscode/" && r.ReIncluded.Contains(Root + "/.vscode/launch.json"));
    }

    [Fact]
    public void A_wildcard_rule_ignores_what_it_does_not_re_include()
    {
        // The matcher itself, against a scaffold that breaks the rule on purpose: a personal file in the
        // ignored folder is an offender, the four re-included files are not.
        var gitignore = new ScaffoldFile(Root + "/.gitignore", "/.vscode/*\n!/.vscode/launch.json\n");
        var result = new ScaffoldResult(
        [
            gitignore,
            new ScaffoldFile(Root + "/.vscode/launch.json", "{}"),
            new ScaffoldFile(Root + "/.vscode/personal.json", "{}"),
        ]);

        var offenders = Offenders(result).ToList();

        Assert.Single(offenders);
        Assert.Contains("personal.json", offenders[0], StringComparison.Ordinal);
    }

    private static void AssertNoOverlap(ScaffoldResult result)
    {
        var offenders = Offenders(result).ToList();

        Assert.True(
            offenders.Count == 0,
            "the scaffold writes files it also gitignores, so a fresh clone loses them:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> Offenders(ScaffoldResult result)
    {
        var paths = result.Files.Select(f => Normalize(f.Path)).ToList();

        foreach (var rule in IgnoreRules(result))
        {
            foreach (var path in paths)
            {
                if (path.StartsWith(rule.Ignored, StringComparison.Ordinal) && !rule.ReIncluded.Contains(path))
                {
                    yield return $"{path} is ignored by '{rule.Entry}' in {rule.Source}";
                }
            }
        }
    }

    /// <summary>
    ///     The directory rules every <c>.gitignore</c> in the scaffold declares — written files and appended
    ///     patches alike — as absolute prefixes, each with the files its own <c>!</c> lines put back.
    /// </summary>
    /// <remarks>
    ///     Two shapes, which are the two the scaffolds write: <c>dir/</c> ignores the whole directory (and
    ///     git cannot re-include a file inside an ignored directory, so nothing is put back), and
    ///     <c>dir/*</c> ignores its contents, where a later <c>!dir/file</c> does put a file back. Entries
    ///     are taken relative to the ignore file's own directory, which is right for the anchored and the
    ///     top-level entries these scaffolds use.
    /// </remarks>
    private static IEnumerable<IgnoreRule> IgnoreRules(ScaffoldResult result)
    {
        var sources = result.Files
            .Where(f => Normalize(f.Path).EndsWith("/.gitignore", StringComparison.Ordinal))
            .Select(f => (f.Path, Text: f.Content))
            .Concat(result.Patches
                .Where(p => p.Path.EndsWith(".gitignore", StringComparison.Ordinal))
                .Select(p => (p.Path, Text: p.Transform(string.Empty))));

        foreach (var (path, text) in sources)
        {
            var scope = Directory(path);
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

            var reIncluded = lines
                .Where(l => l.StartsWith('!'))
                .Select(l => Combine(scope, l[1..]))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entry in lines.Where(l => !l.StartsWith('!')))
            {
                if (entry.EndsWith("/*", StringComparison.Ordinal))
                {
                    yield return new IgnoreRule(path, entry, Combine(scope, entry[..^1]), reIncluded);
                }
                else if (entry.EndsWith('/'))
                {
                    // Git cannot re-include a file inside a directory that is itself ignored.
                    yield return new IgnoreRule(path, entry, Combine(scope, entry), new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }
    }

    private sealed record IgnoreRule(string Source, string Entry, string Ignored, IReadOnlySet<string> ReIncluded);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Directory(string path)
    {
        var normalized = Normalize(path);
        var slash = normalized.LastIndexOf('/');

        return slash < 0 ? string.Empty : normalized[..(slash + 1)];
    }

    private static string Combine(string scope, string entry) =>
        scope + entry.TrimStart('/');
}
