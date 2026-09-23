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

        Assert.Contains(rules, r => r.Entry == "/.vscode/*" && r.ReIncluded.Contains(Root + "/.vscode/launch.json"));
        Assert.Contains(rules, r => r.Matches(Root + "/.vscode/personal.json"));
        Assert.DoesNotContain(rules, r => r.Matches(Root + "/.vscode/launch.json"));
        Assert.DoesNotContain(rules, r => r.Matches(Root + "/.vscode/settings.json"));
    }

    [Fact]
    public void The_matcher_reads_the_file_shaped_entries_too()
    {
        // The blind spot that would have waved appsettings.Development.json through: the matcher only
        // understood dir/ and dir/*, and every entry below is one of the shapes it used to skip. Asserted
        // against the scaffold's real ignore file so it stays true as that file changes.
        var result = ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries(), Version);

        var rules = IgnoreRules(result).ToList();

        Assert.Contains(rules, r => r.Matches(Root + "/appsettings.Development.json"));
        Assert.Contains(rules, r => r.Matches(Root + "/app.db"));
        Assert.Contains(rules, r => r.Matches(Root + "/.env"));
        Assert.Contains(rules, r => r.Matches(Root + "/wwwroot/css/app.css"));

        // …and the tracked files beside them are NOT reported, or the rule would fire for everything.
        Assert.DoesNotContain(rules, r => r.Matches(Root + "/appsettings.json"));
        Assert.DoesNotContain(rules, r => r.Matches(Root + "/.env.example"));
        Assert.DoesNotContain(rules, r => r.Matches(Root + "/Program.cs"));
    }

    [Fact]
    public void The_generated_dev_key_file_is_the_only_exemption_and_it_is_really_written()
    {
        // The exemption pinned from both ends. It has to be earned — the file is actually scaffolded, and
        // is actually ignored — so that deleting either half fails here rather than quietly widening the
        // carve-out this class exists to prevent.
        var result = ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries { Push = true }, Version);

        var path = Root + "/" + WebPushAssembly.DevelopmentSettingsFile;

        Assert.Contains(result.Files, f => Normalize(f.Path) == path);
        Assert.Contains(IgnoreRules(result), r => r.Matches(path));
    }

    [Fact]
    public void A_bang_line_does_not_rescue_a_file_inside_an_ignored_directory()
    {
        // Git's rule, and the one the matcher got wrong on its first pass: `!` re-includes work under
        // `dir/*`, which ignores a directory's CONTENTS, but not under `dir/`, which ignores the
        // directory itself — git never descends into it, so there is nothing for the `!` to put back.
        // Applying re-includes uniformly made the suite report no overlap for a file that is still lost
        // on clone, which is precisely the #957 false green. Both shapes asserted together so the
        // difference between them cannot be flattened again.
        var ignored = new ScaffoldResult(
        [
            new ScaffoldFile(Root + "/.gitignore", "secrets/\n!secrets/keep.txt\n"),
            new ScaffoldFile(Root + "/secrets/keep.txt", "x"),
        ]);

        Assert.Single(Offenders(ignored));

        var contents = new ScaffoldResult(
        [
            new ScaffoldFile(Root + "/.gitignore", "secrets/*\n!secrets/keep.txt\n"),
            new ScaffoldFile(Root + "/secrets/keep.txt", "x"),
        ]);

        Assert.Empty(Offenders(contents));
    }

    [Fact]
    public void A_pwa_app_without_push_gets_no_key_file()
    {
        // The section the keys belong to is conditional on the push battery, so keys written without it
        // would be a secret on disk that nothing reads — and a reader's first evidence that the two had
        // drifted apart.
        var result = ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries { Pwa = true }, Version);

        Assert.DoesNotContain(
            result.Files,
            f => Normalize(f.Path).EndsWith(WebPushAssembly.DevelopmentSettingsFile, StringComparison.Ordinal));
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
                if (rule.Matches(path) && !Exempt.Contains(Relative(path)))
                {
                    yield return $"{path} is ignored by '{rule.Entry}' in {rule.Source}";
                }
            }
        }
    }

    /// <summary>
    ///     The files a scaffold is ALLOWED to write into its own ignore list, each by its exact path
    ///     within the scaffold, and each with the reason it is not the bug this class exists to catch.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rule's cost is that a fresh clone silently loses the file. That is acceptable only for
    ///         a file whose loss degrades rather than breaks, and which the developer who lost it can
    ///         mint again — which is exactly a per-developer secret, and is not true of source like
    ///         <c>push.ts</c>. Anything added here needs that argument made in writing, rather than a
    ///         name added to the list.
    /// </para>
    ///     <para>
    ///         Keyed on the PATH, not the bare file name. A name alone waives it everywhere: a future
    ///         <c>appsettings.Development.json</c> written under <c>client/</c>, or a
    ///         <c>next-env.d.ts</c> in a template that does not regenerate it, would inherit an argument
    ///         that was only ever made for one location.
    ///     </para>
    /// </remarks>
    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
    {
        // The scaffold's development VAPID pair, minted per app by WebPushAssembly. It cannot be
        // committed (a private key shared by every scaffolded app signs nothing), and losing it costs a
        // teammate one regenerated dev key: both the template's Program.cs and RaskBatteryWiring wire
        // the sender only when keys are present, so an app without them starts and runs with push off.
        WebPushAssembly.DevelopmentSettingsFile,

        // Next.js writes this one itself on every `next dev` and `next build` — the file says "should not
        // be edited" in its own body, and create-next-app ships the same pairing of writing it and
        // ignoring it. Regenerated by the toolchain, so a clone that lacks it has it again after one
        // build. Found by extending the matcher below, not introduced with it: it predates this list.
        "client/next-env.d.ts",
    };

    /// <summary>The path of <paramref name="path" /> within the scaffold, for matching <see cref="Exempt" />.</summary>
    private static string Relative(string path)
    {
        var normalized = Normalize(path);
        var root = Root + "/";

        return normalized.StartsWith(root, StringComparison.Ordinal) ? normalized[root.Length..] : normalized;
    }

    /// <summary>
    ///     The rules every ignore file in the scaffold declares — written files and appended patches
    ///     alike — each able to say whether it ignores a given path.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Four shapes, which are the four the scaffolds write. <c>dir/</c> ignores a whole directory
    ///         (git cannot re-include a file inside an ignored directory, so nothing is put back);
    ///         <c>dir/*</c> ignores its contents, where a later <c>!dir/file</c> does put a file back;
    ///         <c>*.ext</c> ignores by extension at any depth; and a bare <c>name</c> ignores that file at
    ///         any depth, or at a fixed place when the entry itself contains a slash.
    ///     </para>
    ///     <para>
    ///         The last two were missing, and their absence was not harmless: most of what this scaffold
    ///         actually ignores is of those shapes — <c>appsettings.Development.json</c>, <c>.env</c>,
    ///         <c>*.db</c>, <c>wwwroot/css/app.css</c> — so a file written to any of them was waved
    ///         through. This class had already been vacuous once (see above); a matcher that reads four
    ///         fifths of the ignore file is how it would have become vacuous again.
    ///     </para>
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

            // A bare `!name` puts that name back wherever it appears, the same way a bare `name` ignores
            // it — otherwise .env.example reads as re-included only at the scaffold root.
            var reIncludedNames = lines
                .Where(l => l.StartsWith('!') && !l.Contains('/', StringComparison.Ordinal))
                .Select(l => l[1..])
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entry in lines.Where(l => !l.StartsWith('!')))
            {
                yield return new IgnoreRule(path, entry, scope, reIncluded, reIncludedNames);
            }
        }
    }

    private sealed record IgnoreRule(
        string Source,
        string Entry,
        string Scope,
        IReadOnlySet<string> ReIncluded,
        IReadOnlySet<string> ReIncludedNames)
    {
        public bool Matches(string path)
        {
            // FIRST, and deliberately ahead of the re-include check: git cannot resurrect a file inside
            // a directory that is itself ignored, so no `!` line rescues anything under a `dir/` rule.
            // Letting re-includes apply here would let a template that ignores `.vscode/` alongside any
            // bare `!name` matching a file inside it report no overlap while the file is still lost on
            // clone — the exact #957 false green this class exists to catch.
            if (Entry.EndsWith('/'))
            {
                return path.StartsWith(Combine(Scope, Entry), StringComparison.Ordinal);
            }

            if (ReIncluded.Contains(path) || ReIncludedNames.Contains(FileName(path)))
            {
                return false;
            }

            if (Entry.EndsWith("/*", StringComparison.Ordinal))
            {
                return path.StartsWith(Combine(Scope, Entry[..^1]), StringComparison.Ordinal);
            }

            // `*.ext` — by extension, at any depth below the ignore file.
            if (Entry.StartsWith("*.", StringComparison.Ordinal))
            {
                return path.StartsWith(Scope, StringComparison.Ordinal)
                       && FileName(path).EndsWith(Entry[1..], StringComparison.Ordinal);
            }

            // Anything else names a file: anchored when the entry contains a slash, matched by name at
            // any depth when it does not, which is git's own rule and the one that covers
            // appsettings.Development.json and .env.
            return Entry.Contains('/', StringComparison.Ordinal)
                ? string.Equals(path, Combine(Scope, Entry), StringComparison.Ordinal)
                : path.StartsWith(Scope, StringComparison.Ordinal)
                  && string.Equals(FileName(path), Entry, StringComparison.Ordinal);
        }
    }

    private static string FileName(string path)
    {
        var normalized = Normalize(path);
        var slash = normalized.LastIndexOf('/');

        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

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
