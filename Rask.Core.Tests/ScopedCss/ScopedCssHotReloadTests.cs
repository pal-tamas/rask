using System.Reflection;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssHotReloadTests
{
    public ScopedCssHotReloadTests() => ScopedCssRegistry.InvalidateAll();

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
    public void UpdateApplication_NullTypes_InvalidatesAll()
    {
        // Null updatedTypes is the "I don't know what changed" signal — drop everything and
        // let module initializers / RefreshAll repopulate from each loaded assembly.
        ScopedCssRegistry.RegisterType(typeof(Reloadable), ".rl { color: red; }");
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        InvokeUpdateApplication(null);

        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void UpdateApplication_UnrelatedType_NoChange()
    {
        // A .cs edit on a regular component class arrives as that type in updatedTypes.
        // The registry's source of truth is now the generator-emitted class, so a lone
        // component-type update is a no-op — entries are not invalidated.
        ScopedCssRegistry.RegisterType(typeof(Reloadable), ".rl { color: red; }");
        var hashBefore = ScopedCssRegistry.CurrentHash;

        var fired = false;
        Action h = () => fired = true;
        ScopedCssRegistry.BundleChanged += h;
        try
        {
            InvokeUpdateApplication(new[] { typeof(Reloadable) });
        }
        finally { ScopedCssRegistry.BundleChanged -= h; }

        Assert.False(fired);
        Assert.Equal(hashBefore, ScopedCssRegistry.CurrentHash);
    }

    [Fact]
    public void UpdateApplication_GeneratedRegistrationType_InvalidatesAll()
    {
        // A .css edit causes the generator to re-emit __RaskScopedCssRegistration, and the
        // hot-reload handler invalidates everything before re-invoking RefreshAll on each
        // loaded assembly. In this test there's no real generated class, so the result is
        // an empty registry.
        ScopedCssRegistry.RegisterType(typeof(Reloadable), ".rl { color: red; }");
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        InvokeUpdateApplication(new[] { typeof(__RaskScopedCssRegistration) });

        Assert.Null(ScopedCssRegistry.CurrentHash);
    }

    private sealed class Reloadable : Component
    {
        protected override Component Render() => this;
    }

    // Hot-reload handler treats any type whose simple name matches the generated class as
    // a signal to refresh. Using a dummy class with the same simple name matches the
    // handler's contract without depending on the actual generator output being present in
    // the test assembly.
    private sealed class __RaskScopedCssRegistration
    {
    }
}
