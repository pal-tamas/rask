using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

public class HostAwarenessTests
{
    [Fact]
    public void DefaultWebServerHost_ReportsWebServerNone()
    {
        var probe = new HostProbe { RenderHandle = new HostHandle() };

        _ = probe.RenderAsLiveRoot();

        Assert.Equal(RenderShell.Web, probe.SeenShell);
        Assert.Equal(RenderEngine.Server, probe.SeenEngine);
        Assert.Equal(RenderPlatform.None, probe.SeenPlatform);
        Assert.False(probe.SeenIsNative);
        Assert.True(probe.SeenIsServer);
        Assert.False(probe.SeenIsWasm);
        Assert.False(probe.SeenIsIOS);
        Assert.False(probe.SeenIsAndroid);
    }

    [Fact]
    public void NativeLocalIosHost_ReportsNativeInProcessIos()
    {
        var probe = new HostProbe
        {
            RenderHandle = new HostHandle
            {
                ShellValue = RenderShell.Native,
                EngineValue = RenderEngine.InProcess,
                PlatformValue = RenderPlatform.IOS,
            },
        };

        _ = probe.RenderAsLiveRoot();

        Assert.Equal(RenderShell.Native, probe.SeenShell);
        Assert.True(probe.SeenIsNative);
        Assert.True(probe.SeenIsIOS);
        Assert.False(probe.SeenIsAndroid);
        Assert.False(probe.SeenIsServer);
        Assert.False(probe.SeenIsWasm);
    }

    [Fact]
    public void NativeServerAndroidHost_ShellAndEngineAreIndependent()
    {
        // Native+Server: the shell is Native AND the engine is Server at the same time — the axes don't collapse.
        var probe = new HostProbe
        {
            RenderHandle = new HostHandle
            {
                ShellValue = RenderShell.Native,
                EngineValue = RenderEngine.Server,
                PlatformValue = RenderPlatform.Android,
            },
        };

        _ = probe.RenderAsLiveRoot();

        Assert.True(probe.SeenIsNative);
        Assert.True(probe.SeenIsServer);
        Assert.True(probe.SeenIsAndroid);
        Assert.False(probe.SeenIsIOS);
    }

    [Fact]
    public void WasmHost_ReportsWasm()
    {
        var probe = new HostProbe
        {
            RenderHandle = new HostHandle { EngineValue = RenderEngine.Wasm },
        };

        _ = probe.RenderAsLiveRoot();

        Assert.True(probe.SeenIsWasm);
        Assert.False(probe.SeenIsServer);
        Assert.False(probe.SeenIsNative);
        Assert.Equal(RenderPlatform.None, probe.SeenPlatform);
    }

    [Fact]
    public void WebHost_IsNeverIos()
    {
        // Invariant: a Web shell never reports a device platform, so IsIOS/IsAndroid stay false.
        var probe = new HostProbe
        {
            RenderHandle = new HostHandle { ShellValue = RenderShell.Web, PlatformValue = RenderPlatform.None },
        };

        _ = probe.RenderAsLiveRoot();

        Assert.False(probe.SeenIsNative);
        Assert.False(probe.SeenIsIOS);
        Assert.False(probe.SeenIsAndroid);
    }

    // A fake render handle reporting fixed host-awareness axes. Rask.Core.Tests has InternalsVisibleTo, so it
    // can implement the internal IRenderHandle axis members explicitly.
    private sealed class HostHandle : IRenderHandle
    {
        public RenderShell ShellValue { get; init; } = RenderShell.Web;
        public RenderEngine EngineValue { get; init; } = RenderEngine.Server;
        public RenderPlatform PlatformValue { get; init; } = RenderPlatform.None;

        public Task RequestRenderAsync() => Task.CompletedTask;

        RenderShell IRenderHandle.Shell => ShellValue;
        RenderEngine IRenderHandle.Engine => EngineValue;
        RenderPlatform IRenderHandle.Platform => PlatformValue;
    }

    private sealed class HostProbe : Component
    {
        public RenderShell SeenShell;
        public RenderEngine SeenEngine;
        public RenderPlatform SeenPlatform;
        public bool SeenIsNative;
        public bool SeenIsServer;
        public bool SeenIsWasm;
        public bool SeenIsIOS;
        public bool SeenIsAndroid;

        protected override Component? Render()
        {
            SeenShell = HostShell;
            SeenEngine = HostEngine;
            SeenPlatform = HostPlatform;
            SeenIsNative = IsNative;
            SeenIsServer = IsServer;
            SeenIsWasm = IsWasm;
            SeenIsIOS = IsIOS;
            SeenIsAndroid = IsAndroid;
            return Text.Value("host-probe");
        }
    }
}
