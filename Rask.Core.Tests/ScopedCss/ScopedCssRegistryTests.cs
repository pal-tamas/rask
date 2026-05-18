using System.Reflection;
using System.Text;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssRegistryTests
{
    public ScopedCssRegistryTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void TryRegister_NullCss_ReturnsFalse()
    {
        var reg = typeof(ScopedCssRegistry);
        var method = reg.GetMethod("TryRegister", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object?[] { new NoCss(), null };
        var result = (bool)method.Invoke(null, args)!;
        Assert.False(result);
        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void TryRegister_NonNullCss_AddsEntryAndBumpsHash()
    {
        var fired = false;
        Action handler = () => fired = true;
        ScopedCssRegistry.BundleChanged += handler;
        try
        {
            CallTryRegister(new HasCss());
            Assert.True(fired);
            Assert.NotNull(ScopedCssRegistry.CurrentHash);
            var (css, hash) = ScopedCssRegistry.GetBundle();
            Assert.Contains(".x[data-r-", css);
            Assert.NotEqual("empty", hash);
        }
        finally { ScopedCssRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void TryRegister_SameTypeTwice_IsIdempotent()
    {
        CallTryRegister(new HasCss());
        var hashAfterFirst = ScopedCssRegistry.CurrentHash;
        var fired = 0;
        Action handler = () => fired++;
        ScopedCssRegistry.BundleChanged += handler;
        try
        {
            CallTryRegister(new HasCss());
            Assert.Equal(0, fired);
            Assert.Equal(hashAfterFirst, ScopedCssRegistry.CurrentHash);
        }
        finally { ScopedCssRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void Invalidate_RemovesEntryAndBumpsHash()
    {
        CallTryRegister(new HasCss());
        var fired = false;
        Action handler = () => fired = true;
        ScopedCssRegistry.BundleChanged += handler;
        try
        {
            ScopedCssRegistry.Invalidate(typeof(HasCss));
            Assert.True(fired);
            Assert.Null(ScopedCssRegistry.CurrentHash);
        }
        finally { ScopedCssRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void GetBundle_ConcatenatesInInsertionOrder()
    {
        CallTryRegister(new HasCss());
        CallTryRegister(new OtherCss());
        var (css, _) = ScopedCssRegistry.GetBundle();
        var xPos = css.IndexOf(".x[data-", StringComparison.Ordinal);
        var yPos = css.IndexOf(".y[data-", StringComparison.Ordinal);
        Assert.True(xPos >= 0 && yPos >= 0);
        Assert.True(xPos < yPos);
    }

    [Fact]
    public void GetBundleUtf8_MatchesGetBundle_AndIsCachedAcrossCalls()
    {
        CallTryRegister(new HasCss());
        CallTryRegister(new OtherCss());

        var (css, hash) = ScopedCssRegistry.GetBundle();
        var (utf8, etag) = ScopedCssRegistry.GetBundleUtf8();

        // Bytes must round-trip to the string bundle exactly.
        Assert.Equal(Encoding.UTF8.GetBytes(css), utf8.ToArray());
        // ETag is the hash wrapped in double quotes (matches the header format).
        Assert.Equal($"\"{hash}\"", etag);

        // The cached buffer is reused across calls until invalidation. ReadOnlyMemory<byte>
        // doesn't expose identity directly; comparing the backing arrays is fine because
        // GetBundleUtf8 stores _cachedBundleUtf8 as a byte[].
        var (utf8b, etagB) = ScopedCssRegistry.GetBundleUtf8();
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(utf8, out var seg1));
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(utf8b, out var seg2));
        Assert.Same(seg1.Array, seg2.Array);
        Assert.Same(etag, etagB);
    }

    [Fact]
    public void GetBundleUtf8_InvalidateBumpsCache()
    {
        CallTryRegister(new HasCss());
        var (_, etag1) = ScopedCssRegistry.GetBundleUtf8();

        CallTryRegister(new OtherCss());
        var (utf8b, etag2) = ScopedCssRegistry.GetBundleUtf8();

        Assert.NotEqual(etag1, etag2);
        Assert.Contains(".y[data-", Encoding.UTF8.GetString(utf8b.ToArray()));
    }

    [Fact]
    public void InvalidateAll_RemovesEverything()
    {
        CallTryRegister(new HasCss());
        CallTryRegister(new OtherCss());
        ScopedCssRegistry.InvalidateAll();
        Assert.Null(ScopedCssRegistry.CurrentHash);
        var (css, _) = ScopedCssRegistry.GetBundle();
        Assert.Empty(css);
    }

    private static void CallTryRegister(Component instance)
    {
        var method = typeof(ScopedCssRegistry).GetMethod(
            "TryRegister",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object?[] { instance, null };
        method.Invoke(null, args);
    }

    private sealed class NoCss : Component
    {
        protected override Component Render() => this;
    }

    private sealed class HasCss : Component
    {
        protected internal override string? Css => ".x { color: red; }";
        protected override Component Render() => this;
    }

    private sealed class OtherCss : Component
    {
        protected internal override string? Css => ".y { color: blue; }";
        protected override Component Render() => this;
    }
}
