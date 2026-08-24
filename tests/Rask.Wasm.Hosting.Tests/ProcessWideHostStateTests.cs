using System.Net;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Hosting.Tests.Infrastructure;

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     Why this assembly runs its test classes serially (see <c>AssemblyInfo.cs</c>): two of the things a
///     host configures are <b>process-wide statics</b>, not per-server state, so two live servers in one
///     process do not have independent ones.
/// </summary>
/// <remarks>
///     <para>
///         This is not an argument, it is a demonstration. The tests below stand two hosts up at once and
///         show the second one taking the first one's state away from it — the first host's own baked
///         asset then 404s, which is exactly the symptom the gate reported in #789:
///         <c>AssetEndpointParityTests.RegistryMiss_BakedBundleFile_NegotiatesPrecompressedSibling —
///         Assert.Equal() Failure: Expected: OK, Actual: NotFound</c>, on a diff that touched none of it.
///     </para>
///     <para>
///         Three classes in this assembly build servers (<c>AssetEndpointParityTests</c>,
///         <c>PathBaseEndpointTests</c>, <c>UseRaskTests</c>) and xUnit runs classes in parallel, so this
///         overlap was reachable on any run. Disposal makes it worse rather than better: it resets the
///         statics, so a host that merely finishes can pull the ground out from under one still serving.
///     </para>
///     <para>
///         The statics are not themselves a bug. A real deployment has one host per process, which is why
///         <c>UseRask</c> can set them at all. It is only a test process that holds two, so the fix
///         belongs to the test assembly — serialising the classes — and not to the product.
///     </para>
/// </remarks>
public sealed class ProcessWideHostStateTests
{
    [Fact]
    public async Task A_second_host_takes_the_baked_bundle_directory_from_the_first()
    {
        using var firstBundle = new FakeBundleDirectory();
        var scopedDir = Path.Combine(firstBundle.Path, "_rask", "a");
        Directory.CreateDirectory(scopedDir);
        const string hash = "0123456789ab";
        File.WriteAllText(Path.Combine(scopedDir, $"{hash}.js"), "window.Rask=window.Rask||{};");

        await using var first = await WasmHostingTestServer.CreateAsync(firstBundle.Path);

        // The first host serves its own baked file, as its own tests assert.
        Assert.Equal(HttpStatusCode.OK, (await first.Http.GetAsync($"/_rask/a/{hash}.js")).StatusCode);

        // A second host — another test class, in parallel — points the one static somewhere else.
        using var secondBundle = new FakeBundleDirectory();
        await using (await WasmHostingTestServer.CreateAsync(secondBundle.Path))
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await first.Http.GetAsync($"/_rask/a/{hash}.js")).StatusCode);
        }

        // …and disposing the second one clears the static outright, so the first host stays broken.
        Assert.Null(ScopedAssetBundle.BakedDirectory);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await first.Http.GetAsync($"/_rask/a/{hash}.js")).StatusCode);
    }

    [Fact]
    public async Task A_second_host_takes_the_path_base_from_the_first()
    {
        using var bundle = new FakeBundleDirectory();
        await using var first = await WasmHostingTestServer.CreateAsync(bundle.Path, pathBase: "/app");

        Assert.Equal("/app", LiveOptions.PathBase);

        using var secondBundle = new FakeBundleDirectory();
        await using (await WasmHostingTestServer.CreateAsync(secondBundle.Path))
        {
            // The second host configures no prefix, and the first host's is gone — one slot, not two.
            Assert.NotEqual("/app", LiveOptions.PathBase);
        }
    }
}
