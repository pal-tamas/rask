using Rask.Core.Messaging;

namespace Rask.Core.Tests.Messaging;

// The Flash service is a thread-safe, consumed-once FIFO queue. These pin the queue semantics
// independently of any rendering: draining, the once-only guarantee, id assignment, the Changed
// signal, the level-convenience methods, and concurrent producers.
public class FlashTests
{
    [Fact]
    public void Add_ThenConsume_ReturnsTheMessage()
    {
        IFlash flash = new Flash();
        flash.Add(FlashLevel.Success, "Saved", "Done");

        var drained = flash.Consume();

        var msg = Assert.Single(drained);
        Assert.Equal(FlashLevel.Success, msg.Level);
        Assert.Equal("Saved", msg.Message);
        Assert.Equal("Done", msg.Title);
    }

    [Fact]
    public void Consume_DrainsOnce()
    {
        IFlash flash = new Flash();
        flash.Info("hello");

        Assert.Single(flash.Consume());
        // Second consume sees an empty queue — the message was delivered exactly once.
        Assert.Empty(flash.Consume());
    }

    [Fact]
    public void Consume_WhenEmpty_ReturnsEmptyNotNull()
    {
        IFlash flash = new Flash();

        var drained = flash.Consume();

        Assert.NotNull(drained);
        Assert.Empty(drained);
    }

    [Fact]
    public void Ids_AreMonotonic_AndPreserveOrder()
    {
        IFlash flash = new Flash();
        flash.Info("a");
        flash.Info("b");
        flash.Info("c");

        var drained = flash.Consume();

        Assert.Equal(["a", "b", "c"], drained.Select(m => m.Message));
        Assert.Equal(drained.OrderBy(m => m.Id).Select(m => m.Id), drained.Select(m => m.Id));
        Assert.Equal(3, drained.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Add_RaisesChanged()
    {
        IFlash flash = new Flash();
        var fired = 0;
        flash.Changed += () => fired++;

        flash.Warning("careful");

        Assert.Equal(1, fired);
    }

    [Theory]
    [InlineData(nameof(IFlash.Info), FlashLevel.Info)]
    [InlineData(nameof(IFlash.Success), FlashLevel.Success)]
    [InlineData(nameof(IFlash.Warning), FlashLevel.Warning)]
    [InlineData(nameof(IFlash.Error), FlashLevel.Error)]
    public void ConvenienceMethods_SetTheMatchingLevel(string method, FlashLevel expected)
    {
        IFlash flash = new Flash();
        switch (method)
        {
            case nameof(IFlash.Info): flash.Info("m"); break;
            case nameof(IFlash.Success): flash.Success("m"); break;
            case nameof(IFlash.Warning): flash.Warning("m"); break;
            case nameof(IFlash.Error): flash.Error("m"); break;
        }

        Assert.Equal(expected, Assert.Single(flash.Consume()).Level);
    }

    [Fact]
    public void Add_IsThreadSafe_UnderConcurrentProducers()
    {
        IFlash flash = new Flash();
        const int n = 1000;

        Parallel.For(0, n, i => flash.Add(FlashLevel.Info, i.ToString()));

        var drained = flash.Consume();
        // No lost updates and no duplicate ids despite concurrent adds.
        Assert.Equal(n, drained.Count);
        Assert.Equal(n, drained.Select(m => m.Id).Distinct().Count());
    }
}
