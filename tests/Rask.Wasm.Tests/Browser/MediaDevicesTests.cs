using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class MediaDevicesTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.isSupported", true);

        Assert.True(await new MediaDevices(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Enumerate_ReturnsDevices()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.enumerate", new[]
        {
            new MediaDeviceInfo("cam1", "videoinput", "FaceTime HD", "g1")
        });

        var devices = await new MediaDevices(js).EnumerateDevicesAsync();

        Assert.Single(devices);
        Assert.Equal("videoinput", devices[0].Kind);
    }

    [Fact]
    public async Task GetUserMedia_PassesConstraints_AndReturnsHandle()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getUserMedia", 5);
        var constraints = new MediaConstraints(Video: true, Audio: true);

        var handle = await new MediaDevices(js).GetUserMediaAsync(constraints);

        Assert.Same(constraints, js.ArgsFor("__raskMedia.getUserMedia")![0]);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task GetDisplayMedia_ReturnsHandle()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getDisplayMedia", 7);

        var handle = await new MediaDevices(js).GetDisplayMediaAsync();

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task Attach_PassesIdAndElementRef()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getUserMedia", 9);
        var video = ElementRef.New();
        var handle = await new MediaDevices(js).GetUserMediaAsync(new MediaConstraints());

        await handle.AttachToAsync(video);

        Assert.Equal([9, video], js.ArgsFor("__raskMedia.attach"));
    }

    [Fact]
    public async Task Stop_PassesId_AndIsIdempotent()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getUserMedia", 3);
        var handle = await new MediaDevices(js).GetUserMediaAsync(new MediaConstraints());

        await handle.StopAsync();
        await handle.StopAsync();

        Assert.Equal(1, js.CallCount("__raskMedia.stop"));
        Assert.Equal([3], js.ArgsFor("__raskMedia.stop"));
    }

    [Fact]
    public async Task Dispose_StopsTracks_Once()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getUserMedia", 4);
        var handle = await new MediaDevices(js).GetUserMediaAsync(new MediaConstraints());

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskMedia.stop"));
    }

    [Fact]
    public async Task GetUserMedia_NullConstraints_Throws()
    {
        var svc = new MediaDevices(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.GetUserMediaAsync(null!));
    }

    [Fact]
    public async Task Attach_NullVideo_Throws()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskMedia.getUserMedia", 1);
        var handle = await new MediaDevices(js).GetUserMediaAsync(new MediaConstraints());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await handle.AttachToAsync(null!));
    }
}
