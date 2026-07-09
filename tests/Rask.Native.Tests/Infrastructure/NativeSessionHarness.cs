using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;

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
        string initialPath = "/", Action<IServiceCollection>? configure = null) =>
        NewSessionAsync<NativeStubApp>(initialPath, configure);

    public static async Task<(NativeApp app, FakeNativeWebView webView, byte[] initialFrame)> NewSessionAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string initialPath = "/", Action<IServiceCollection>? configure = null)
        where TApp : Component
    {
        // CreateDefault() leaves LiveOptions.DiffMode at whatever ResettingTestBase pinned it to.
        var host = NativeAppHost.CreateDefault();
        configure?.Invoke(host.Services);

        var webView = new FakeNativeWebView();
        var app = await host.RunLocalAsync<TApp>(webView, initialPath);

        // The client posts {type:"ready"} once loaded; that triggers the first render.
        await webView.PostAsync("""{"type":"ready"}""");

        return (app, webView, webView.Frames.Count > 0 ? webView.LastFrame : Array.Empty<byte>());
    }
}

/// <summary>Serializes native session tests — they mutate process-global <c>LiveOptions.DiffMode</c>.</summary>
[CollectionDefinition("NativeSession")]
public sealed class NativeSessionCollection;
