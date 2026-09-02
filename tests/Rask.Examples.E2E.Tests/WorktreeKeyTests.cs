using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// Several worktrees run this suite on one machine, each in its own process, and the on-demand publish
// directory under $TMPDIR used to be keyed on the app name alone — so they all resolved to the same
// folder and could publish different commits into it. The in-process `lock` in ExampleAppFixture cannot
// reach across processes, so the PATH is what has to differ. These pin that it does.
public class WorktreeKeyTests
{
    [Fact]
    public void Two_checkouts_never_share_a_publish_directory()
    {
        var main = ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask");
        var linked = ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask/.claude/worktrees/a");

        Assert.NotEqual(main, linked);
    }

    // Worktree folder names are chosen by whoever created them and repeat freely across clones, which is
    // why the key is derived from the absolute path rather than the last segment.
    [Fact]
    public void Same_worktree_name_under_different_clones_still_differs()
    {
        var first = ExampleAppFixture.WorktreeKey("/Users/x/one/.claude/worktrees/feature");
        var second = ExampleAppFixture.WorktreeKey("/Users/x/two/.claude/worktrees/feature");

        Assert.NotEqual(first, second);
    }

    // Stable across calls and across processes: the fallback publish is reused between runs from the same
    // checkout, so a key that moved would strand a multi-hundred-megabyte publish in $TMPDIR every time.
    [Fact]
    public void The_key_is_stable_for_one_checkout()
    {
        Assert.Equal(
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask"),
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask"));
    }

    // A "." segment is the same checkout, and must not produce a second copy.
    [Fact]
    public void Equivalent_spellings_of_one_path_agree()
    {
        Assert.Equal(
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask"),
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/./Rask"));
    }

    // So is a trailing separator — and this one needs its own row rather than riding along with the
    // "." case, because Path.GetFullPath does NOT normalise it away. Asserting only the "." spelling
    // let the comment claim a property the code did not have.
    [Fact]
    public void A_trailing_separator_is_the_same_checkout()
    {
        Assert.Equal(
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask"),
            ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask/"));
    }

    // Short enough that $TMPDIR stays readable when someone goes looking, and hex so it is always a legal
    // path segment on every platform.
    [Fact]
    public void The_key_is_eight_lowercase_hex_characters()
    {
        var key = ExampleAppFixture.WorktreeKey("/Users/x/RiderProjects/Rask");

        Assert.Equal(8, key.Length);
        Assert.All(key, c => Assert.True(char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'), $"'{c}' is not lowercase hex"));
    }
}
