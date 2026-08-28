namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The commit-message gate names a config file that exists.
/// </summary>
/// <remarks>
///     <para>
///         <c>wagoid/commitlint-github-action</c> resolves its <c>configFile</c> input with
///         <c>existsSync</c> and, when that is false, falls back to
///         <c>@commitlint/config-conventional</c> with <b>no error and no warning</b>. So a rename
///         that misses the workflow does not turn the gate red — it leaves it green, enforcing
///         conventional defaults instead of this repository's rules. The header-length limit, the
///         type list and the subject-case rule would all quietly stop applying.
///     </para>
///     <para>
///         That is the failure this test exists for, and it is why the check is on the two files
///         agreeing rather than on either one alone. It runs here because this is the suite that
///         owns the repository's TypeScript, and the config is now TypeScript.
///     </para>
/// </remarks>
public class CommitlintConfigTests
{
    [Fact]
    public void TheWorkflowNamesAConfigFileThatExists()
    {
        var root = RepositoryRoot();
        var workflow = Path.Combine(root, ".github", "workflows", "commitlint.yml");

        Assert.True(File.Exists(workflow), $"'{workflow}' is missing — the commit-message gate has gone.");

        var text = File.ReadAllText(workflow);
        var marker = "configFile:";
        var index = text.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(
            index >= 0,
            "commitlint.yml no longer passes configFile, so the action will use its own default path. "
            + "Either name the config explicitly or move this assertion to whatever replaced it.");

        var named = text[(index + marker.Length)..]
            .Split('\n')[0]
            .Trim();

        Assert.True(
            File.Exists(Path.Combine(root, named)),
            $"commitlint.yml points at '{named}', which does not exist. The action falls back to "
            + "@commitlint/config-conventional silently when that happens, so the gate would stay "
            + "green while enforcing none of this repository's rules.");
    }

    [Fact]
    public void TheConfigIsTypeScript_AndDeclaresItsTypeImportAsAType()
    {
        var config = Path.Combine(RepositoryRoot(), "commitlint.config.ts");

        Assert.True(File.Exists(config), "commitlint.config.ts is missing.");

        var text = File.ReadAllText(config);

        // The action does not install this repository's dependencies, so the workspace has no
        // node_modules. jiti elides an `import type`; a value-position import of @commitlint/types
        // would be a runtime resolution failure inside the action.
        Assert.DoesNotContain("import {UserConfig}", text, StringComparison.Ordinal);
        Assert.Contains("import type", text, StringComparison.Ordinal);

        // .ts, not .mts: @commitlint/load 19.x registers loaders for .ts and .cts only. The
        // documentation lists .mts because it describes a later major than the action ships.
        Assert.False(
            File.Exists(Path.Combine(RepositoryRoot(), "commitlint.config.mts")),
            "commitlint.config.mts will not be loaded by @commitlint/load 19.x — rename it to .ts.");
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
