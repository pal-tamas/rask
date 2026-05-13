using System.Reflection;
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
