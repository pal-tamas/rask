using Rask.Server.Http;

namespace Rask.Server.Tests.Http;

// A copy of a public page is served to everyone who is nobody. It may be kept by the visitor's own browser
// and must never be kept by anything shared — the same line an anonymous static page already draws.
public class ShellCachePolicyStoredCopyTests
{
    [Fact]
    public void AStoredCopy_IsPrivateAndRevalidated_NeverNoStore()
    {
        var decision = ShellCachePolicy.ForStoredCopy();

        Assert.DoesNotContain("no-store", decision.CacheControl, StringComparison.Ordinal);
        Assert.Contains("private", decision.CacheControl, StringComparison.Ordinal);
        Assert.Contains("must-revalidate", decision.CacheControl, StringComparison.Ordinal);
        Assert.Null(decision.Pragma);
    }

    [Fact]
    public void AStoredCopy_VariesOnTheCookie()
    {
        // Whether a signed-in visitor gets the copy at all is decided by the cookie.
        Assert.Equal("Cookie", ShellCachePolicy.ForStoredCopy().Vary);
    }

    [Fact]
    public void AStoredCopy_IsCachedExactlyAsAnAnonymousStaticPageIs()
    {
        // Two routes to the same kind of response; a browser should not be able to tell them apart.
        Assert.Equal(
            ShellCachePolicy.For(interactive: false, authenticated: false, faulted: false, statusCode: 200),
            ShellCachePolicy.ForStoredCopy());
    }
}
