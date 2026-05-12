using System.Reflection;
using Rask.Core.ScopedCss;

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssHotReloadTests
{
    public ScopedCssHotReloadTests() => ScopedCssRegistry.InvalidateAll();

    private static void Register(Component instance)
    {
        var m = typeof(ScopedCssRegistry).GetMethod(
            "TryRegister",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        m.Invoke(null, new object?[] { instance, null });
    }

    private static void InvokeUpdateApplication(Type[]? types)
    {
        var handlerType = typeof(ScopedCssRegistry).Assembly
            .GetType("Rask.Core.ScopedCss.ScopedCssHotReloadHandler", true)!;
        var update = handlerType.GetMethod(
            "UpdateApplication",
            BindingFlags.Public | BindingFlags.Static)!;
        update.Invoke(null, new object?[] { types });
    }

    [Fact]
    public void UpdateApplication_TargetedType_InvalidatesEntry()
    {
        Register(new Reloadable());
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        var fired = false;
        Action h = () => fired = true;
        ScopedCssRegistry.BundleChanged += h;
        try
        {
            InvokeUpdateApplication(new[] { typeof(Reloadable) });
        }
        finally { ScopedCssRegistry.BundleChanged -= h; }

        Assert.True(fired);
        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void UpdateApplication_NullTypes_InvalidatesAll()
    {
        Register(new Reloadable());
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        InvokeUpdateApplication(null);

        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void UpdateApplication_UnknownType_NoChange()
    {
        Register(new Reloadable());
        var hashBefore = ScopedCssRegistry.CurrentHash;

        var fired = false;
        Action h = () => fired = true;
        ScopedCssRegistry.BundleChanged += h;
        try
        {
            InvokeUpdateApplication(new[] { typeof(string) });
        }
        finally { ScopedCssRegistry.BundleChanged -= h; }

        Assert.False(fired);
        Assert.Equal(hashBefore, ScopedCssRegistry.CurrentHash);
    }

    private sealed class Reloadable : Component
    {
        protected internal override string? Css => ".rl { color: red; }";
        protected override Component Render() => this;
    }
}
