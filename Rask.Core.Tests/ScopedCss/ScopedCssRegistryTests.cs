using System.Runtime.InteropServices;
using System.Text;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssRegistryTests
{
    public ScopedCssRegistryTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void EmptyRegistry_HasNullHash()
    {
        Assert.Null(ScopedCssRegistry.CurrentHash);
        var (css, _) = ScopedCssRegistry.GetBundle();
        Assert.Empty(css);
    }

    [Fact]
    public void RegisterType_AddsEntryAndBumpsHash()
    {
        var fired = false;
        Action handler = () => fired = true;
        ScopedCssRegistry.BundleChanged += handler;
        try
        {
            ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
            Assert.True(fired);
            Assert.NotNull(ScopedCssRegistry.CurrentHash);
            var (css, hash) = ScopedCssRegistry.GetBundle();
            Assert.Contains(".x[data-r-", css);
            Assert.NotEqual("empty", hash);
        }
        finally { ScopedCssRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void RegisterType_NullOrWhitespace_DoesNotAddEntry()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), "");
        Assert.Null(ScopedCssRegistry.CurrentHash);
        ScopedCssRegistry.RegisterType(typeof(HasCss), "   \n  ");
        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void RegisterType_SameCssTwice_IsIdempotent()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
        var hashAfterFirst = ScopedCssRegistry.CurrentHash;
        var fired = 0;
        Action handler = () => fired++;
        ScopedCssRegistry.BundleChanged += handler;
        try
        {
            ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
            Assert.Equal(0, fired);
            Assert.Equal(hashAfterFirst, ScopedCssRegistry.CurrentHash);
        }
        finally { ScopedCssRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void Invalidate_RemovesEntryAndBumpsHash()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
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
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(OtherCss), ".y { color: blue; }");
        var (css, _) = ScopedCssRegistry.GetBundle();
        var xPos = css.IndexOf(".x[data-", StringComparison.Ordinal);
        var yPos = css.IndexOf(".y[data-", StringComparison.Ordinal);
        Assert.True(xPos >= 0 && yPos >= 0);
        Assert.True(xPos < yPos);
    }

    [Fact]
    public void GetBundleUtf8_MatchesGetBundle_AndIsCachedAcrossCalls()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(OtherCss), ".y { color: blue; }");

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
        Assert.True(MemoryMarshal.TryGetArray(utf8, out var seg1));
        Assert.True(MemoryMarshal.TryGetArray(utf8b, out var seg2));
        Assert.Same(seg1.Array, seg2.Array);
        Assert.Same(etag, etagB);
    }

    [Fact]
    public void GetBundleUtf8_InvalidateBumpsCache()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
        var (_, etag1) = ScopedCssRegistry.GetBundleUtf8();

        ScopedCssRegistry.RegisterType(typeof(OtherCss), ".y { color: blue; }");
        var (utf8b, etag2) = ScopedCssRegistry.GetBundleUtf8();

        Assert.NotEqual(etag1, etag2);
        Assert.Contains(".y[data-", Encoding.UTF8.GetString(utf8b.ToArray()));
    }

    [Fact]
    public void InvalidateAll_RemovesEverything()
    {
        ScopedCssRegistry.RegisterType(typeof(HasCss), ".x { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(OtherCss), ".y { color: blue; }");
        ScopedCssRegistry.InvalidateAll();
        Assert.Null(ScopedCssRegistry.CurrentHash);
        var (css, _) = ScopedCssRegistry.GetBundle();
        Assert.Empty(css);
    }

    private sealed class HasCss : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class OtherCss : Component
    {
        protected override RenderResult Render() => this;
    }
}
