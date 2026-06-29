using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class MediaSessionTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMediaSession.isSupported", true);

        Assert.True(await new MediaSession(js).IsSupportedAsync());
    }

    [Fact]
    public async Task SetMetadata_PassesRecord()
    {
        var js = new FakeJsRuntime();
        var meta = new MediaMetadata
        {
            Title = "Song",
            Artist = "Band",
            Album = "LP",
            Artwork = [new MediaArtwork("art.png", "512x512", "image/png")]
        };

        await new MediaSession(js).SetMetadataAsync(meta);

        Assert.Same(meta, js.ArgsFor("__raskMediaSession.setMetadata")![0]);
    }

    [Theory]
    [InlineData(PlaybackState.Playing, "playing")]
    [InlineData(PlaybackState.Paused, "paused")]
    [InlineData(PlaybackState.None, "none")]
    public async Task SetPlaybackState_MapsToken(PlaybackState state, string token)
    {
        var js = new FakeJsRuntime();

        await new MediaSession(js).SetPlaybackStateAsync(state);

        Assert.Equal([token], js.ArgsFor("__raskMediaSession.setPlaybackState"));
    }

    [Theory]
    [InlineData(MediaSessionAction.Play, "play")]
    [InlineData(MediaSessionAction.PreviousTrack, "previoustrack")]
    [InlineData(MediaSessionAction.SeekForward, "seekforward")]
    public async Task SetActionHandler_RegistersTokenAndRoutes(MediaSessionAction action, string token)
    {
        var js = new FakeJsRuntime();
        var fired = 0;

        await new MediaSession(js).SetActionHandlerAsync(action, () =>
        {
            fired++;
            return Task.CompletedTask;
        });

        var args = js.ArgsFor("__raskMediaSession.setActionHandler");
        Assert.IsType<int>(args![0]);
        Assert.Equal(token, args[1]);

        var id = (int)args[0]!;
        await MediaSessionInterop.Invoke(id);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Dispose_RemovesHandler_AndStopsRouting()
    {
        var js = new FakeJsRuntime();
        var fired = 0;
        var handle = await new MediaSession(js).SetActionHandlerAsync(MediaSessionAction.Pause, () =>
        {
            fired++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskMediaSession.setActionHandler")![0]!;

        await handle.DisposeAsync();
        await MediaSessionInterop.Invoke(id); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskMediaSession.removeActionHandler"));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task NullArgs_Throw()
    {
        var svc = new MediaSession(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.SetMetadataAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.SetActionHandlerAsync(MediaSessionAction.Play, null!));
    }
}
