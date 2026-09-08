using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Nothing the scaffolder writes may land in a directory the scaffolder also gitignores.
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
///         file added to a generated directory. Every framework and both battery shapes are checked, so
///         a template that grows a hand-owned file in the wrong place fails here rather than on somebody
///         else's second clone. (#957)
///     </para>
/// </remarks>
public sealed class ScaffoldIgnoreOverlapTests
{
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

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void No_scaffolded_file_lands_in_a_directory_the_scaffold_ignores(string key)
    {
        Assert.True(SpaFramework.TryGet(key, out var framework));

        // --push is the battery that put a hand-owned file in a generated directory, so it is the one
        // that has to be on. Pwa comes with it (--push implies --pwa) and brings its own client files.
        var result = ProjectGenerator.GenerateSpa(
            "/scaffold-root",
            "Shop",
            framework,
            new ServerBatteries { Push = true },
            Version);

        var offenders = new List<string>();

        foreach (var patch in result.Patches)
        {
            if (!patch.Path.EndsWith(".gitignore", StringComparison.Ordinal))
            {
                continue;
            }

            // The ignore file's own directory: entries in it are relative to that, not to the repo root.
            var scope = Directory(patch.Path);

            foreach (var entry in IgnoredDirectories(patch))
            {
                var ignored = Combine(scope, entry);

                offenders.AddRange(
                    result.Files
                        .Select(f => Normalize(f.Path))
                        .Where(p => p.StartsWith(ignored, StringComparison.Ordinal))
                        .Select(p => $"{p} is ignored by '{entry}' in {patch.Path}"));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "the scaffold writes files into a directory it also gitignores, so a fresh clone loses them:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_push_helper_is_still_scaffolded_when_push_is_asked_for()
    {
        // Guards the cheap way to pass the test above: not writing the file at all. It has to be both
        // present AND outside the ignored directory.
        var result = ProjectGenerator.GenerateSpa(
            "/scaffold-root",
            "Shop",
            SpaFramework.React,
            new ServerBatteries { Push = true },
            Version);

        Assert.Contains(result.Files, f => Normalize(f.Path).EndsWith("push.ts", StringComparison.Ordinal));
    }

    /// <summary>The directory entries a <c>.gitignore</c> patch appends, as written.</summary>
    private static IEnumerable<string> IgnoredDirectories(ScaffoldPatch patch) =>
        patch.Transform(string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.EndsWith('/'));

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
