using Rask.Core;
using Rask.Core.Browser;
using Rask.Core.Tests.Interop;

namespace Rask.Core.Tests.Browser;

public class MediaStreamsTests
{
    [Fact]
    public async Task AttachAsync_PassesTheRawIdAndTheElementRef()
    {
        var js = new FakeJsRuntime();
        var video = ElementRef.New();

        await new MediaStreams(js).AttachAsync(new MediaStreamId(7), video);

        // The raw int, not the wrapper: __raskMedia keys its map by number, and a serialized
        // { "value": 7 } would miss every entry.
        Assert.Equal([7, video], js.ArgsFor("__raskMedia.attach"));
    }

    [Fact]
    public async Task StopAsync_PassesTheRawId()
    {
        var js = new FakeJsRuntime();

        await new MediaStreams(js).StopAsync(new MediaStreamId(4));

        Assert.Equal([4], js.ArgsFor("__raskMedia.stop"));
    }

    [Fact]
    public async Task AttachAsync_RejectsANullElement()
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new MediaStreams(js).AttachAsync(new MediaStreamId(1), null!).AsTask());
    }

    [Fact]
    public void MediaStreamId_IsValueEqual()
    {
        // It travels through callbacks and dictionary keys; reference semantics would break both.
        Assert.Equal(new MediaStreamId(3), new MediaStreamId(3));
        Assert.NotEqual(new MediaStreamId(3), new MediaStreamId(4));
    }
}
