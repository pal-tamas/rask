using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

// #1184: the WASM service worker stored every successful same-origin GET, so a signed-in visitor's
// `/api/…` answers stayed in Cache Storage after sign-out and were replayed, offline, to the next
// person at that browser. The Cache Storage API honours no header on its own, so the rule is the
// worker's — driven here under Node through the function its fetch handler calls (OfflineCacheFixture.ts).
public sealed class OfflineCacheTests
{
    [Theory]
    [InlineData("navigation")]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("image")]
    [InlineData("runtime")]
    [InlineData("runtimeUnderSubPath")]
    [InlineData("islandManifest")]
    public void The_app_shell_is_kept_for_offline_use(string request)
    {
        if (Kept() is not { } kept)
        {
            return;
        }

        Assert.True(kept.GetProperty(request).GetBoolean());
    }

    [Theory]
    [InlineData("api")]
    [InlineData("apiMarkedPrivate")]
    [InlineData("navigationMarkedNoStore")]
    [InlineData("scriptMarkedPrivate")]
    [InlineData("failedNavigation")]
    public void A_response_that_is_data_or_marked_as_one_persons_is_not_kept(string request)
    {
        if (Kept() is not { } kept)
        {
            return;
        }

        Assert.False(kept.GetProperty(request).GetBoolean());
    }

    [Fact]
    public void Data_is_kept_once_the_server_marks_it_public()
    {
        if (Kept() is not { } kept)
        {
            return;
        }

        Assert.True(kept.GetProperty("apiMarkedPublic").GetBoolean());
    }

    // Null with no node on PATH — deliberately not a failure: node is not required to build or test Rask.
    private static JsonElement? Kept() => NodeFixture.Run("OfflineCacheFixture")?.GetProperty("kept");
}
