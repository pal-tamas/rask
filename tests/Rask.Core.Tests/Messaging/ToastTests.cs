using Rask.Core.Messaging;

namespace Rask.Core.Tests.Messaging;

// The Toaster service is a thread-safe, consumed-once FIFO queue. These pin the queue semantics
// independently of any rendering: draining, the once-only guarantee, id assignment, the Changed
// signal, the level-convenience methods, and concurrent producers.
public class ToastTests
{
    [Fact]
    public void A_consume_after_an_add_returns_the_message()
    {
        IToaster toast = new Toaster();
        toast.Add(ToastLevel.Success, "Saved", "Done");

        var drained = toast.Consume();

        var msg = Assert.Single(drained);
        Assert.Equal(ToastLevel.Success, msg.Level);
        Assert.Equal("Saved", msg.Message);
        Assert.Equal("Done", msg.Title);
    }

    [Fact]
    public void Consuming_drains_the_queue_once()
    {
        IToaster toast = new Toaster();
        toast.Info("hello");

        Assert.Single(toast.Consume());

        // Second consume sees an empty queue — the message was delivered exactly once.
        Assert.Empty(toast.Consume());
    }

    [Fact]
    public void Consuming_an_empty_queue_returns_empty_not_null()
    {
        IToaster toast = new Toaster();

        var drained = toast.Consume();

        Assert.NotNull(drained);
        Assert.Empty(drained);
    }

    [Fact]
    public void Ids_are_monotonic_and_preserve_order()
    {
        IToaster toast = new Toaster();
        toast.Info("a");
        toast.Info("b");
        toast.Info("c");

        var drained = toast.Consume();

        Assert.Equal(["a", "b", "c"], drained.Select(m => m.Message));
        Assert.Equal(drained.OrderBy(m => m.Id).Select(m => m.Id), drained.Select(m => m.Id));
        Assert.Equal(3, drained.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Adding_a_message_raises_Changed()
    {
        IToaster toast = new Toaster();
        var fired = 0;
        toast.Changed += () => fired++;

        toast.Warning("careful");

        Assert.Equal(1, fired);
    }

    [Theory]
    [InlineData(nameof(IToaster.Info), ToastLevel.Info)]
    [InlineData(nameof(IToaster.Success), ToastLevel.Success)]
    [InlineData(nameof(IToaster.Warning), ToastLevel.Warning)]
    [InlineData(nameof(IToaster.Error), ToastLevel.Error)]
    public void The_convenience_methods_set_the_matching_level(string method, ToastLevel expected)
    {
        IToaster toast = new Toaster();
        switch (method)
        {
            case nameof(IToaster.Info): toast.Info("m"); break;
            case nameof(IToaster.Success): toast.Success("m"); break;
            case nameof(IToaster.Warning): toast.Warning("m"); break;
            case nameof(IToaster.Error): toast.Error("m"); break;
        }

        Assert.Equal(expected, Assert.Single(toast.Consume()).Level);
    }

    [Fact]
    public void Adding_is_thread_safe_under_concurrent_producers()
    {
        IToaster toast = new Toaster();
        const int n = 1000;

        Parallel.For(0, n, i => toast.Add(ToastLevel.Info, i.ToString()));

        var drained = toast.Consume();
        // No lost updates and no duplicate ids despite concurrent adds.
        Assert.Equal(n, drained.Count);
        Assert.Equal(n, drained.Select(m => m.Id).Distinct().Count());
    }
}
