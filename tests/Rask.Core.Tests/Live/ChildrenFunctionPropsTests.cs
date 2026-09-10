#pragma warning disable RASK014 // test-defined Component subclasses are built through the chain, not `new`

namespace Rask.Core.Tests.Live;

// A component whose props come from a CHILDREN-FUNCTION's argument (#1050).
//
// `Form.Model(m)[submitting => [ … ]]` calls its children function on every render with whether a submit
// is in flight. A kit component in there whose prop derives from that flag kept its old value for the
// whole submit: the button written as `submitting ? "Saving…" : "Sign up"` stayed "Sign up".
//
// The cause is a commit-point mismatch rather than anything about forms. A children function runs during
// the SERIALIZER's walk, while the deferred entry commit — the drain of pending resets plus
// NotifyParameters — runs at the end of the owner's RenderForLive, which by then is long past. So the
// component the function built never had PropsDirty set, and the render cache handed back the subtree
// from before the argument changed.
//
// It read correctly with an element child (`Button[submitting ? … : …]`) because an element's children
// are walked, never cached, which is why the showcase never hit it until it moved onto Rask.Ui. An
// explicit `Key` also hid it, by resolving to a different instance — which no call site should have to
// know.
public partial class ChildrenFunctionPropsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void AComponentBuiltInAChildrenFunction_SeesTheNewArgument()
    {
        var flag = false;
        var view = new StubComponent(() => AmbientHost.Flag(flag)[
            on => [AmbientLabel.Text(on ? "on" : "off")]
        ]);

        Assert.Contains(">off<", view.RenderAsLiveRoot(), StringComparison.Ordinal);

        flag = true;
        var after = view.RenderAsLiveRoot();

        // Fails before the fix with ">off<": the label component served its cached render.
        Assert.Contains(">on<", after, StringComparison.Ordinal);
        Assert.DoesNotContain(">off<", after, StringComparison.Ordinal);
    }

    [Fact]
    public void ItRunsTheLifecycleHookRatherThanOnlyRepainting()
    {
        // The prop change has to arrive as a prop change, not merely as different markup: a component
        // that acts on OnPropsChanged (resetting a scroll position, restarting a timer) is as much a
        // caller of this as one that only renders its value.
        var flag = false;
        var view = new StubComponent(() => AmbientHost.Flag(flag)[
            on => [AmbientProbe.Text(on ? "on" : "off")]
        ]);

        view.RenderAsLiveRoot();
        var afterFirst = AmbientProbe.PropsChangedCount;

        flag = true;
        view.RenderAsLiveRoot();

        Assert.Equal(afterFirst + 1, AmbientProbe.PropsChangedCount);
    }

    [Fact]
    public void AChildrenFunctionThatYields_IsMaterialisedBeforeItsChildrenAreWalked()
    {
        // A `yield` body builds each entry as the walk reaches it. Committing before the walk is only
        // meaningful if the sequence has been materialised first — otherwise every child is committed
        // one child too late, which is the same bug wearing a different hat.
        var flag = false;
        var view = new StubComponent(() => AmbientHost.Flag(flag)[Lazy]);

        Assert.Contains(">off<", view.RenderAsLiveRoot(), StringComparison.Ordinal);

        flag = true;
        Assert.Contains(">on<", view.RenderAsLiveRoot(), StringComparison.Ordinal);

        static IEnumerable<Component?> Lazy(bool on)
        {
            yield return AmbientLabel.Text(on ? "on" : "off");
        }
    }
}

// Top-level rather than nested: the generator injects an entry named after the component into every
// markup host, and a nested type of the same name would collide with it (CS0102).
//
// Stands in for a form: an element that stores a children function and calls it with an ambient value on
// every render. Nothing here is form-specific, which is the point — the defect is in the commit point,
// not in Form.
public sealed partial class AmbientHost : Element
{
    private Func<bool, IEnumerable<Component?>>? _factory;

    public bool Flag { get; set; }

    protected override string? TagName => "div";

    public Component this[Func<bool, IEnumerable<Component?>> children]
    {
        get
        {
            _factory = children;
            return this;
        }
    }

    protected override IEnumerable<Component?> RenderChildren() =>
        _factory is { } factory ? factory(Flag) : base.RenderChildren();
}

public sealed partial class AmbientLabel : Component
{
    public string? Text { get; set; }

    protected override Component? Render() => Span[Text ?? ""];
}

public sealed partial class AmbientProbe : Component
{
    public string? Text { get; set; }

    internal static int PropsChangedCount { get; private set; }

    protected override void OnPropsChanged() => PropsChangedCount++;

    protected override Component? Render() => Span[Text ?? ""];
}
