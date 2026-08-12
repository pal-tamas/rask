using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// IPermissions is the API you reach for BEFORE calling IGeolocation/INotifications/IClipboard, to decide
// whether you are about to prompt. On a native head those three resolve to backends gated by the OS app
// permission, so IPermissions has to resolve to the native backend too — answering from the WebView would
// be a question about one system answered by another, and it would look authoritative while doing it.
//
// The real modules (ApplePlatform / AndroidPlatform) need the iOS/Android workloads and don't build here,
// so this pins the wiring contract they depend on: a platform-registered IPermissions must survive the
// browser-API fallbacks the host wires afterwards. That is the failure mode worth a test — the backend
// silently shadowed by the JS default, with everything still compiling and running.
[Collection("NativeSession")]
public sealed class NativePermissionsRegistrationTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task NativeHost_WithNoPlatform_FallsBackToTheJsBackedDefault()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync();

        // A head that registers no platform still gets an IPermissions — the WebView one. That is the
        // documented fallback, and the state issue #639 described as the whole surface.
        Assert.IsType<Permissions>(app.Services.GetService<IPermissions>());
    }

    [Fact]
    public async Task UsePlatform_NativeBackend_WinsOverTheJsDefault()
    {
        var host = NativeAppHost.CreateDefault();
        host.UsePlatform(new FakePlatform());
        var webView = new FakeNativeWebView();

        var app = await host.RunLocalAsync<NativeStubApp>(webView);
        await webView.PostAsync("""{"type":"ready"}""");

        var resolved = Assert.IsType<FakeNativePermissions>(app.Services.GetService<IPermissions>());
        Assert.NotNull(resolved.Js);
    }

    [Fact]
    public async Task AppRegistration_BeforeRun_WinsOverTheJsDefault()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync(
            configure: s => s.AddSingleton<IPermissions>(
                sp => new FakeNativePermissions(sp.GetRequiredService<IJSRuntime>())));

        Assert.IsType<FakeNativePermissions>(app.Services.GetService<IPermissions>());
    }

    // Stand-in for ApplePlatform / AndroidPlatform, registering IPermissions the way they do: TryAdd (so it
    // wins over the JS fallback the host wires afterward) from a factory that resolves IJSRuntime.
    //
    // That resolve is the part worth pinning. The real backends answer Camera/Microphone by delegating to
    // the WebView's own Permissions API, so they need an IJSRuntime at construction — and a platform's
    // Register runs BEFORE the host has finished wiring. It works only because the factory defers to first
    // resolve; registering IJSRuntime any later, or constructing eagerly, breaks every native head at
    // startup with a resolution failure that no unit test would otherwise see.
    private sealed class FakePlatform : INativePlatform
    {
        public void Register(IServiceCollection services) =>
            services.TryAddSingleton<IPermissions>(sp =>
                new FakeNativePermissions(sp.GetRequiredService<IJSRuntime>()));
    }

    private sealed class FakeNativePermissions(IJSRuntime js) : IPermissions
    {
        public IJSRuntime Js { get; } = js;

        public ValueTask<PermissionState> QueryAsync(PermissionName name) =>
            ValueTask.FromResult(PermissionState.Granted);
    }
}
