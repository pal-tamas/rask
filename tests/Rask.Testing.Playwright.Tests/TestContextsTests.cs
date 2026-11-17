namespace Rask.Testing.Playwright.Tests;

public sealed class TestContextsTests
{
    private TestScope? _now;

    [Fact]
    public async Task A_test_s_contexts_are_closed_once_the_next_test_of_its_lane_opens_one()
    {
        var closed = new List<string>();
        var contexts = new TestContexts(() => _now);
        _now = new TestScope("lane", "first");
        contexts.Opened("one", Closing(closed, "one"));
        contexts.Opened("two", Closing(closed, "two"));

        _now = new TestScope("lane", "second");
        await contexts.CloseFinished();

        Assert.Equal(["one", "two"], closed);
        Assert.Equal(0, contexts.Count);
    }

    [Fact]
    public async Task A_test_running_in_another_lane_keeps_its_contexts_open()
    {
        var closed = new List<string>();
        var contexts = new TestContexts(() => _now);
        _now = new TestScope("left", "first");
        contexts.Opened("one", Closing(closed, "one"));

        _now = new TestScope("right", "second");
        await contexts.CloseFinished();

        Assert.Empty(closed);
        Assert.Equal(1, contexts.Count);
    }

    [Fact]
    public async Task A_test_opening_more_contexts_keeps_its_own_open()
    {
        var closed = new List<string>();
        var contexts = new TestContexts(() => _now);
        _now = new TestScope("lane", "first");
        contexts.Opened("one", Closing(closed, "one"));

        await contexts.CloseFinished();

        Assert.Empty(closed);
        Assert.Equal(1, contexts.Count);
    }

    [Fact]
    public async Task A_context_the_test_closed_itself_is_not_closed_again()
    {
        var closed = new List<string>();
        var contexts = new TestContexts(() => _now);
        _now = new TestScope("lane", "first");
        contexts.Opened("one", Closing(closed, "one"));
        contexts.Closed("one");

        _now = new TestScope("lane", "second");
        await contexts.CloseFinished();

        Assert.Empty(closed);
    }

    [Fact]
    public async Task A_context_opened_outside_a_test_is_left_to_close_with_the_browser()
    {
        var closed = new List<string>();
        var contexts = new TestContexts(() => _now);
        contexts.Opened("fixture's", Closing(closed, "fixture's"));

        _now = new TestScope("lane", "first");
        await contexts.CloseFinished();

        Assert.Empty(closed);
        Assert.Equal(0, contexts.Count);
    }

    [Fact]
    public void The_test_running_now_is_read_from_xunit_by_its_test_and_its_collection()
    {
        var context = TestContext.Current;

        var scope = TestScope.Current();

        Assert.Equal(new TestScope(context.TestCollection!.UniqueID, context.Test!.UniqueID), scope);
    }

    [Fact]
    public void Outside_a_test_there_is_no_test_running()
    {
        var context = (object?)null;

        var scope = TestScope.Of(context);

        Assert.Null(scope);
    }

    private static Func<Task> Closing(List<string> closed, string name) => () =>
    {
        closed.Add(name);
        return Task.CompletedTask;
    };
}
