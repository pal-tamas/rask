namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The commit-message gate and the local hook apply ONE rule, from one file.
/// </summary>
/// <remarks>
///     <para>
///         <c>scripts/lib/conventional-commit.sh</c> states the allowed types and the header limit;
///         <c>.githooks/commit-msg</c> applies it to every local commit and
///         <c>.github/workflows/commitlint.yml</c> to every commit of a pull request and its title. Own
///         work is pushed straight to main, so the hook is the only place the rule ever meets it, and the
///         workflow the only place it meets an external contribution.
///     </para>
///     <para>
///         The failure this pins is drift: a second copy of the type list or the limit in either caller,
///         which then disagrees with the first the day one of them is edited — or a workflow that goes back
///         to a Node action and a config file of its own. The repository has no Node anywhere.
///     </para>
/// </remarks>
public class CommitlintConfigTests
{
    private const string RuleFile = "scripts/lib/conventional-commit.sh";
    private const string RuleFunction = "rask_commit_header_error";

    [Fact]
    public void The_rule_file_states_the_types_and_the_header_limit_exactly_once()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), RuleFile));

        var types = Occurrences(text, "RASK_COMMIT_TYPES=");
        var limit = Occurrences(text, "RASK_COMMIT_MAX_HEADER=");

        Assert.Equal(1, types);
        Assert.Equal(1, limit);
        Assert.Contains("RASK_COMMIT_MAX_HEADER=100", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".githooks/commit-msg")]
    [InlineData(".github/workflows/commitlint.yml")]
    public void A_caller_sources_the_shared_rule_and_restates_none_of_it(string caller)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), caller));

        var restated = text.Contains("feat|fix", StringComparison.Ordinal)
            || text.Contains("RASK_COMMIT_TYPES=", StringComparison.Ordinal)
            || text.Contains("RASK_COMMIT_MAX_HEADER=", StringComparison.Ordinal);

        Assert.Contains(RuleFile, text, StringComparison.Ordinal);
        Assert.Contains(RuleFunction, text, StringComparison.Ordinal);
        Assert.False(restated, $"{caller} restates the type list or the header limit; read them from {RuleFile}.");
    }

    [Fact]
    public void The_gate_runs_no_node_tool_and_reads_no_commitlint_config()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "commitlint.yml"));

        var configs = Directory.EnumerateFiles(root, "commitlint.config.*").ToList();

        Assert.DoesNotContain("commitlint-github-action", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("setup-node", workflow, StringComparison.Ordinal);
        Assert.Empty(configs);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
