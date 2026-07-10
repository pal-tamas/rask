using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native.Tests.Session;

// IShare is provided by the native host out of the box (the JS-backed Share, over the WebView bridge), and
// a platform head overrides it with a native backend (UIActivityViewController / ACTION_SEND) by registering
// its own on host.Services before RunLocalAsync. AddSingleton is last-wins, so the head's registration takes
// precedence — the contract the rask-native template heads rely on.
public sealed class NativeShareRegistrationTests
{
    [Fact]
    public void NativeHost_ResolvesIShare_ToJsBackedDefault()
    {
        var host = NativeAppHost.CreateDefault();

        using var provider = host.Services.BuildServiceProvider();

        Assert.IsType<Share>(provider.GetService<IShare>());
    }

    [Fact]
    public void HeadRegistration_BeforeRun_OverridesTheDefault_LastWins()
    {
        var host = NativeAppHost.CreateDefault();
        host.Services.AddSingleton<IShare, FakeNativeShare>();

        using var provider = host.Services.BuildServiceProvider();

        Assert.IsType<FakeNativeShare>(provider.GetService<IShare>());
    }

    // Stand-in for a platform head's native NativeShare (the real one lives in the rask-native template,
    // which needs the iOS/Android workloads and isn't built in CI).
    private sealed class FakeNativeShare : IShare
    {
        public ValueTask ShareAsync(ShareData data) => default;

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }
}
