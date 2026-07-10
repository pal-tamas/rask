using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Native.Tests.Infrastructure;

/// <summary>
///     Builds a Native + Local app wired the way the session tests need it: a <see cref="NativeAppHost" />
///     driving a <see cref="FakeNativeWebView" />, with the boot <c>ready</c> handshake already posted so
///     the first frame has been rendered. Imported as a static using so call sites read
///     <c>NewSessionAsync()</c> / <c>NewSessionAsync&lt;App&gt;()</c>.
/// </summary>
internal static class NativeSessionHarness
{
    public static Task<(NativeApp app, FakeNativeWebView webView, byte[] initialFrame)> NewSessionAsync(
        string initialPath = "/", Action<IServiceCollection>? configure = null,
        LiveDiffMode diffMode = LiveDiffMode.Auto) =>
        NewSessionAsync<NativeStubApp>(initialPath, configure, diffMode);

    public static async Task<(NativeApp app, FakeNativeWebView webView, byte[] initialFrame)> NewSessionAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string initialPath = "/", Action<IServiceCollection>? configure = null,
        LiveDiffMode diffMode = LiveDiffMode.Auto)
        where TApp : Component
    {
        // Per-session wire shape (was the process-global LiveOptions.DiffMode the tests pinned).
        var host = NativeAppHost.CreateDefault(o => o.DiffMode = diffMode);
        configure?.Invoke(host.Services);

        var webView = new FakeNativeWebView();
        var app = await host.RunLocalAsync<TApp>(webView, initialPath);

        // The client posts {type:"ready"} once loaded; that triggers the first render.
        await webView.PostAsync("""{"type":"ready"}""");

        return (app, webView, webView.Frames.Count > 0 ? webView.LastFrame : Array.Empty<byte>());
    }
}

/// <summary>
///     Serializes native session tests — they reset the process-global
///     <c>ScopedAssetRegistry</c> (via <c>ResettingTestBase</c>), which is shared process-wide.
///     (DiffMode is per-session now, so that is no longer a reason to serialize.)
/// </summary>
[CollectionDefinition("NativeSession")]
public sealed class NativeSessionCollection;
