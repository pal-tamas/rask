using System.Runtime.InteropServices;
using System.Text;
using Rask.Core.ScopedJs;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedJs;

[Collection("ScopedJs")]
public class ScopedJsRegistryTests
{
    public ScopedJsRegistryTests() => ScopedJsRegistry.InvalidateAll();

    [Fact]
    public void EmptyRegistry_HasNullHash()
    {
        Assert.Null(ScopedJsRegistry.CurrentHash);
        var (js, _) = ScopedJsRegistry.GetBundle();
        Assert.Empty(js);
    }

    [Fact]
    public void RegisterType_AddsEntryAndBumpsHash()
    {
        var fired = false;
        Action handler = () => fired = true;
        ScopedJsRegistry.BundleChanged += handler;
        try
        {
            ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
            Assert.True(fired);
            Assert.NotNull(ScopedJsRegistry.CurrentHash);
            var (js, hash) = ScopedJsRegistry.GetBundle();
            Assert.Contains("Rask.scoped.register(", js);
            Assert.Contains("function rendered(el) {}", js);
            // The wrapper returns the rendered function directly (or undefined when absent).
            Assert.Contains("typeof rendered === 'function'", js);
            // Wrapper no longer references mount/unmount — those were the prior contract.
            Assert.DoesNotContain("typeof mount === 'function'", js);
            Assert.DoesNotContain("typeof unmount === 'function'", js);
            // The export keyword is stripped before wrapping so the function is in the
            // closure scope of the register() factory.
            Assert.DoesNotContain("export function", js);
            Assert.NotEqual("empty", hash);
        }
        finally { ScopedJsRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void RegisterType_IncludesScopeIdInWrapper()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        var (js, _) = ScopedJsRegistry.GetBundle();
        // Scope ids are formed by CssScoper.ScopeIdFor — `r-` + 8 hex chars.
        Assert.Matches(@"Rask\.scoped\.register\(""r-[0-9a-f]{8}""", js);
    }

    [Fact]
    public void RegisterType_IsRegistered_ReturnsTrueAfterRegister()
    {
        Assert.False(ScopedJsRegistry.IsRegistered(typeof(HasJs)));
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        Assert.True(ScopedJsRegistry.IsRegistered(typeof(HasJs)));
    }

    [Fact]
    public void RegisterType_NullOrWhitespace_DoesNotAddEntry()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "");
        Assert.Null(ScopedJsRegistry.CurrentHash);
        ScopedJsRegistry.RegisterType(typeof(HasJs), "   \n  ");
        Assert.Null(ScopedJsRegistry.CurrentHash);
        Assert.False(ScopedJsRegistry.IsRegistered(typeof(HasJs)));
    }

    [Fact]
    public void RegisterType_SameSourceTwice_IsIdempotent()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        var hashAfterFirst = ScopedJsRegistry.CurrentHash;
        var fired = 0;
        Action handler = () => fired++;
        ScopedJsRegistry.BundleChanged += handler;
        try
        {
            ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
            Assert.Equal(0, fired);
            Assert.Equal(hashAfterFirst, ScopedJsRegistry.CurrentHash);
        }
        finally { ScopedJsRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void UnregisterType_RemovesEntryAndBumpsHash()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        var fired = false;
        Action handler = () => fired = true;
        ScopedJsRegistry.BundleChanged += handler;
        try
        {
            ScopedJsRegistry.UnregisterType(typeof(HasJs));
            Assert.True(fired);
            Assert.Null(ScopedJsRegistry.CurrentHash);
            Assert.False(ScopedJsRegistry.IsRegistered(typeof(HasJs)));
        }
        finally { ScopedJsRegistry.BundleChanged -= handler; }
    }

    [Fact]
    public void GetBundle_ConcatenatesInInsertionOrder()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) { el.id = 'x'; }");
        ScopedJsRegistry.RegisterType(typeof(OtherJs), "export function rendered(el) { el.id = 'y'; }");
        var (js, _) = ScopedJsRegistry.GetBundle();
        var xPos = js.IndexOf("el.id = 'x'", StringComparison.Ordinal);
        var yPos = js.IndexOf("el.id = 'y'", StringComparison.Ordinal);
        Assert.True(xPos >= 0 && yPos >= 0);
        Assert.True(xPos < yPos);
    }

    [Fact]
    public void GetBundleUtf8_MatchesGetBundle_AndIsCachedAcrossCalls()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        ScopedJsRegistry.RegisterType(typeof(OtherJs), "export function rendered(el) {}");

        var (js, hash) = ScopedJsRegistry.GetBundle();
        var (utf8, etag) = ScopedJsRegistry.GetBundleUtf8();

        Assert.Equal(Encoding.UTF8.GetBytes(js), utf8.ToArray());
        Assert.Equal($"\"{hash}\"", etag);

        var (utf8b, etagB) = ScopedJsRegistry.GetBundleUtf8();
        Assert.True(MemoryMarshal.TryGetArray(utf8, out var seg1));
        Assert.True(MemoryMarshal.TryGetArray(utf8b, out var seg2));
        Assert.Same(seg1.Array, seg2.Array);
        Assert.Same(etag, etagB);
    }

    [Fact]
    public void InvalidateAll_RemovesEverything()
    {
        ScopedJsRegistry.RegisterType(typeof(HasJs), "export function rendered(el) {}");
        ScopedJsRegistry.RegisterType(typeof(OtherJs), "export function rendered(el) {}");
        ScopedJsRegistry.InvalidateAll();
        Assert.Null(ScopedJsRegistry.CurrentHash);
        var (js, _) = ScopedJsRegistry.GetBundle();
        Assert.Empty(js);
    }

    private sealed class HasJs : Component
    {
        protected override Component Render() => this;
    }

    private sealed class OtherJs : Component
    {
        protected override Component Render() => this;
    }
}
