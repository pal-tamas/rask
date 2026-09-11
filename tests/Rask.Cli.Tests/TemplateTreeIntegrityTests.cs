using System.Diagnostics;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     The committed template trees are intact: every file is tracked, every template has one, and
///     nothing in them pins a version that should be a token.
/// </summary>
public sealed class TemplateTreeIntegrityTests
{
    private static readonly string RepoRoot = CliBuildE2E.FindRepoRoot();
    private static string TemplateRoot => Path.Combine(RepoRoot, "src", "Rask.Templates");

    [Fact]
    public void Every_template_file_is_tracked()
    {
        // The templates are payload, and the repository's own ignore rules reach into them. Two
        // separate mechanisms have already dropped files here with no error of any kind:
        //
        //   * the root .gitignore's `.vscode/` line, which matches at any depth, silently excluded the
        //     editor recommendations create-vite and the Angular CLI ship (eight files);
        //   * a template's OWN .gitignore, which the VCS reads as a rule for THIS repository and which
        //     beats the root file — angular's `.vscode/` and next's `next-env.d.ts`. Those are stored
        //     without their leading dot for exactly this reason (see TemplateMaterializer.InertNames).
        //
        // An untracked template file is invisible: the tree looks complete on the machine that made it
        // and scaffolds a broken app for everyone else. Only comparing disk against the index catches
        // it, so that is what this does.
        var onDisk = Directory
            .EnumerateFiles(TemplateRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(RepoRoot, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(onDisk);

        var tracked = Vcs("ls-files --cached --others --exclude-standard -- src/Rask.Templates")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var untracked = onDisk.Where(p => !tracked.Contains(p)).ToArray();

        Assert.True(
            untracked.Length == 0,
            $"{untracked.Length} template file(s) are excluded by an ignore rule, so they are not "
            + "committed and no scaffold will ever write them:\n  " + string.Join("\n  ", untracked)
            + "\n\nEither negate the rule in the root .gitignore (a directory has to be re-included, "
            + "not just the files under it), or store the offending file inert the way gitignore is.");
    }

    [Fact]
    public void Every_catalog_template_has_a_committed_tree()
    {
        var missing = TemplateCatalog.Keys
            .Where(key => !TemplateMaterializer.Has(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These templates are offered by `rask new` and have no committed tree, so choosing one "
            + $"scaffolds nothing:\n  {string.Join("\n  ", missing)}");
    }

    [Fact]
    public void No_template_pins_a_literal_Rask_version()
    {
        // A literal version here would pin every scaffolded app to whatever the CLI happened to be when
        // the template was extracted, which is a package that does not exist yet on the day it ships.
        var offenders = Directory
            .EnumerateFiles(TemplateRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, index))
                .Where(l => l.line.Contains("Include=\"Rask.", StringComparison.Ordinal)
                    && l.line.Contains("Version=\"", StringComparison.Ordinal)
                    && !l.line.Contains(TemplateMaterializer.VersionToken, StringComparison.Ordinal)))
            .Select(l => $"{Path.GetRelativePath(RepoRoot, l.path)}:{l.index + 1} {l.line.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These template package references state a version instead of "
            + $"{TemplateMaterializer.VersionToken}:\n  {string.Join("\n  ", offenders)}");
    }

    [Fact]
    public void Every_template_tree_has_a_manifest()
    {
        var missing = Directory
            .EnumerateDirectories(TemplateRoot)
            // _islands is not a template: it holds the per-runtime FRAGMENTS that --islands merges into
            // a host template, so it has no battery conditionals of its own to record. Each fragment
            // carries an island.json instead, which is asserted separately.
            .Where(d => !Path.GetFileName(d).StartsWith('_'))
            .Where(d => !File.Exists(Path.Combine(d, TemplateMaterializer.ManifestFile)))
            .Select(d => Path.GetFileName(d))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"These template trees have no {TemplateMaterializer.ManifestFile}, so nothing records "
            + $"which files a battery owns:\n  {string.Join("\n  ", missing)}");
    }

    [Fact]
    public void Every_front_end_template_can_be_re_imported()
    {
        // A committed tree is a snapshot, and scripts/refresh-templates.sh is the whole answer to it
        // going stale: it re-runs the framework's own creator and shows the diff. A template missing
        // from that script has no way back to upstream, and nothing would say so — it would simply
        // drift until someone noticed the starter no longer looked like the framework's own.
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "refresh-templates.sh"));

        var missing = SpaFramework.All.Select(f => f.Key)
            .Concat(MetaTemplate.All.Select(f => f.Key))
            .Where(key => !script.Contains($"{key})", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "scripts/refresh-templates.sh names no creator for these templates, so there is no way to "
            + $"pull a newer upstream into them:\n  {string.Join("\n  ", missing)}");
    }

    private static string Vcs(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
