using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     The feed's render log: whole commits, oldest first, a component's first render called its mount, and bounded by
///     commits and by renders without ever dropping the newest commit.
/// </summary>
public sealed class DevToolsCommitLogTests
{
#pragma warning disable RASK014 // components made by hand, standing in for the ones a walk would report
    private static Component Child(string? key = null) => key is null ? new DevToolsTestChild() : new DevToolsTestChild { Key = key };
#pragma warning restore RASK014

    private static List<DevToolsRenderItem> Renders(params (Component Component, RenderCause Cause)[] renders) =>
        renders.Select(r => new DevToolsRenderItem(r.Component, r.Cause, SelfTicks: 10)).ToList();

    [Fact]
    public void A_components_first_render_is_its_mount_and_later_ones_carry_their_cause()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
        var child = Child("k1");

        feed.RecordCommit(Renders((child, RenderCause.Props)), walked: 3, ids, timestamp: 1);
        feed.RecordCommit(Renders((child, RenderCause.State)), walked: 3, ids, timestamp: 2);

        var commits = feed.CommitsSnapshot();
        Assert.Equal(2, commits.Length);
        var first = Assert.Single(commits[0].Renders);
        var second = Assert.Single(commits[1].Renders);
        Assert.Equal(DevToolsRenderReason.Mount, first.Reason);
        Assert.Equal(DevToolsRenderReason.State, second.Reason);
        // The same component, named the way the Tree tab names it, keyed on the same id.
        Assert.Equal(ids.IdOf(child), first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(nameof(DevToolsTestChild), first.Type);
        Assert.Equal("k1", first.Key);
        Assert.Equal(3, commits[1].Walked);
        Assert.True(commits[1].Sequence > commits[0].Sequence);
    }

    [Fact]
    public void A_commit_in_which_nothing_rendered_is_still_a_commit()
    {
        var feed = new DevToolsFeed();

        feed.RecordCommit([], walked: 12, new DevToolsTreeSnapshotter(), timestamp: 1);

        var commit = Assert.Single(feed.CommitsSnapshot());
        Assert.Empty(commit.Renders);
        Assert.Equal(12, commit.Walked);
    }

    [Fact]
    public void Past_the_commit_capacity_the_oldest_commits_go_first()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
        var child = Child();

        for (var i = 0; i < DevToolsFeed.CommitCapacity + 3; i++)
        {
            feed.RecordCommit(Renders((child, RenderCause.State)), walked: i, ids, timestamp: i);
        }

        var commits = feed.CommitsSnapshot();
        Assert.Equal(DevToolsFeed.CommitCapacity, commits.Length);
        Assert.Equal(3, commits[0].Walked);
        Assert.Equal(DevToolsFeed.CommitCapacity + 2, commits[^1].Walked);
    }

    [Fact]
    public void Past_the_render_capacity_old_commits_go_but_the_newest_stays_however_big()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
        var small = Renders((Child(), RenderCause.State));
        var huge = Enumerable.Range(0, DevToolsFeed.RenderCapacity + 1)
            .Select(_ => new DevToolsRenderItem(Child(), RenderCause.Props, 1)).ToList();

        feed.RecordCommit(small, walked: 1, ids, timestamp: 1);
        feed.RecordCommit(huge, walked: huge.Count, ids, timestamp: 2);

        var commit = Assert.Single(feed.CommitsSnapshot());
        Assert.Equal(DevToolsFeed.RenderCapacity + 1, commit.Renders.Length);
    }

    [Fact]
    public void Clear_forgets_the_commits_but_not_which_components_have_mounted()
    {
        var feed = new DevToolsFeed();
        var ids = new DevToolsTreeSnapshotter();
        var child = Child();
        var notified = 0;
        feed.Changed += () => notified++;

        feed.RecordCommit(Renders((child, RenderCause.Uncached)), walked: 1, ids, timestamp: 1);
        feed.ClearCommits();
        Assert.Empty(feed.CommitsSnapshot());

        feed.RecordCommit(Renders((child, RenderCause.Uncached)), walked: 1, ids, timestamp: 2);
        Assert.Equal(DevToolsRenderReason.Uncached, Assert.Single(Assert.Single(feed.CommitsSnapshot()).Renders).Reason);
        Assert.Equal(3, notified);
    }
}
