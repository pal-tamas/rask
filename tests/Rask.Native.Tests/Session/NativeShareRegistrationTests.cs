using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// IShare is provided by the native host out of the box (the JS-backed Share, over the WebView bridge). The
// framework wires the browser-API fallbacks in RunLocalAsync AFTER any native platform (UsePlatform) or app
// registration, and every tier uses TryAdd — so a native backend, or an app registration made before the
// run, wins (native-first) and the JS Share is only the fallback. This is the contract the rask-native
// template heads rely on.
[Collection("NativeSession")]
public sealed class NativeShareRegistrationTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task NativeHost_ResolvesIShare_ToJsBackedDefault()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync();

        Assert.IsType<Share>(app.Services.GetService<IShare>());
    }

    [Fact]
    public async Task AppRegistration_BeforeRun_WinsOverTheJsDefault()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync(
            configure: s => s.AddSingleton<IShare, FakeNativeShare>());

        Assert.IsType<FakeNativeShare>(app.Services.GetService<IShare>());
    }

    [Fact]
    public async Task UsePlatform_NativeBackend_WinsOverTheJsDefault()
    {
        var host = NativeAppHost.CreateDefault();
        host.UsePlatform(new FakePlatform());
        var webView = new FakeNativeWebView();

        var app = await host.RunLocalAsync<NativeStubApp>(webView);
        await webView.PostAsync("""{"type":"ready"}""");

        Assert.IsType<FakeNativeShare>(app.Services.GetService<IShare>());
    }

    // Stand-in for a real platform module (ApplePlatform / AndroidPlatform), which need the iOS/Android
    // workloads and aren't built in CI. It registers a native backend the same way — TryAdd, so it wins
    // over the JS fallback the host wires afterward.
    private sealed class FakePlatform : INativePlatform
    {
        public void Register(IServiceCollection services) =>
            services.AddBrowserApi<IShare, FakeNativeShare>(ServiceLifetime.Singleton);
    }

    private sealed class FakeNativeShare : IShare
    {
        public ValueTask ShareAsync(ShareData data) => default;

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }
}
