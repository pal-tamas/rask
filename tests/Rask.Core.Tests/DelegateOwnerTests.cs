using Rask.Core;

#pragma warning disable RASK014 // directly constructing the component under test, not rendering it

namespace Rask.Core.Tests;

// Regression coverage for DelegateOwner.Resolve — the owner a DOM handler re-renders after it runs.
public class DelegateOwnerTests
{
    private sealed class OwnerComponent : Component
    {
        public string? Touched;

        public void Handle(string value) => Touched = value;

        // A method group — target IS the component (fast path, no reflection).
        public Action MethodGroupHandler() => () => Handle("m");

        // A this-only lambda — Roslyn lowers to an instance method, target IS the component.
        public Action ThisOnlyHandler()
        {
            var local = "x";
            return () => Handle(local.Length.ToString());
        }

        // The DriversPage/VehiclesPage shape: a per-row handler built inside a Select lambda that closes
        // over the row item AND `this`. Roslyn lowers this to NESTED display classes — the delegate's
        // immediate target holds the row item; the captured `this` lives on an OUTER display class — so
        // the direct `<>4__this` lookup misses. This is the case that regressed (handler fell back to the
        // composite element as owner, so the defining component never re-rendered).
        // Two nested lambdas where the INNER captures the outer lambda's local AND `this` — like
        // DriversPage's `drivers.Select(d => … OnClick: () => OpenEdit(d))`. Roslyn lowers this to nested
        // display classes: the inner lambda's immediate target holds the local, while the captured `this`
        // lives on the OUTER display class, so a direct `<>4__this` lookup misses it.
        public Action NestedHandler()
        {
            Action inner = null!;
            Action outer = () =>
            {
                var local = "captured";
                inner = () => Handle(local);
            };
            outer();
            return inner;
        }
    }

    [Fact]
    public void Resolve_MethodGroup_ReturnsComponent()
    {
        var component = new OwnerComponent();
        Assert.Same(component, DelegateOwner.Resolve(component.MethodGroupHandler()));
    }

    [Fact]
    public void Resolve_ThisOnlyClosure_ReturnsComponent()
    {
        var component = new OwnerComponent();
        Assert.Same(component, DelegateOwner.Resolve(component.ThisOnlyHandler()));
    }

    [Fact]
    public void Resolve_LoopCapturedNestedClosure_ReturnsDefiningComponent()
    {
        var component = new OwnerComponent();
        Assert.Same(component, DelegateOwner.Resolve(component.NestedHandler()));
    }
}
