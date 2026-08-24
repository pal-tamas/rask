using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// Handler ids are assigned per (rendering component, local slot) and stick for that component's
// lifetime. The invariant every test here defends: WHAT ONE COMPONENT RENDERS CANNOT RENUMBER
// ANOTHER'S HANDLERS. Before this, ids came from a single per-render counter reset to 0 each frame,
// so one component gaining a handler shifted every later handler on the page — rewriting
// data-rask-on-* on untouched elements, and forcing the clean-subtree cache to re-walk them.
//
// Each case asserts the id AND that dispatch still lands on the right delegate, because a stable id
// that resolves to the wrong handler is worse than an unstable one.
public partial class StableHandlerIdTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement EmptyPayload => JsonDocument.Parse("{}").RootElement;

    // The handler id on the element carrying `cls`. Attribute order puts class before the
    // data-rask-on-* hooks (Element.WriteAttributes), so this stays anchored to the right element.
    private static string IdOn(string html, string cls)
    {
        var m = Regex.Match(html, $"class=\"{cls}\"[^>]*?data-rask-on-[a-z]+=\"([^\"]+)\"");
        Assert.True(m.Success, $"no handler hook on .{cls} in: {html}");
        return m.Groups[1].Value;
    }

    private static int NumberOf(string handlerId) =>
        int.Parse(handlerId.AsSpan(1), CultureInfo.InvariantCulture);

    // ---- One component gaining a handler must not renumber another's -----------------------------

    private sealed class Toggler : Component
    {
        public int ExtraClicks;
        public int OwnClicks;
        private bool _expanded;

        public void Expand()
        {
            _expanded = true;
            StateHasChanged();
        }

        protected override Component? Render() => _expanded
            ? Div.Class("t-wrap")[
                Div.Class("t-extra").OnClick(() => ExtraClicks++)["extra"],
                Div.Class("t-own").OnClick(() => OwnClicks++)["own"]
            ]
            : Div.Class("t-wrap")[Div.Class("t-own").OnClick(() => OwnClicks++)["own"]];
    }

    private sealed class Steady : Component
    {
        public int Clicks;

        protected override Component? Render() =>
            Div.Class("steady").OnClick(() => Clicks++)["steady"];
    }

    [Fact]
    public async Task UnchangedComponentKeepsItsId_WhenAnotherComponentGainsAHandler()
    {
        var toggler = new Toggler();
        var steady = new Steady();
        var root = new StubComponent(() => Div.Class("page")[toggler, steady]);

        var before = root.RenderAsLiveRoot();
        var steadyId = IdOn(before, "steady");

        // The toggler now renders one MORE handler, and renders it BEFORE `steady` in walk order —
        // the exact shape that used to push every later id up by one.
        toggler.Expand();
        var after = root.RenderAsLiveRoot();

        Assert.Equal(steadyId, IdOn(after, "steady"));
        Assert.True(await root.TryInvokeHandlerAsync(steadyId, EmptyPayload), "the unshifted id must resolve");
        Assert.Equal(1, steady.Clicks);
        Assert.Equal(0, toggler.ExtraClicks);

        // ...and the handler the toggler gained is live too, on an id of its own.
        var extraId = IdOn(after, "t-extra");
        Assert.NotEqual(steadyId, extraId);
        Assert.True(await root.TryInvokeHandlerAsync(extraId, EmptyPayload));
        Assert.Equal(1, toggler.ExtraClicks);
    }

    // ---- A component's own slots stay put across its own re-renders ------------------------------

    private sealed class ThreeHandlers : Component
    {
        public int CountA;
        public int CountB;
        public int CountC;
        public int Version;

        public void Touch()
        {
            Version++;
            StateHasChanged();
        }

        protected override Component? Render() =>
            Div.Class("three")[
                Div.Class("h-a").OnClick(() => CountA++)[$"a{Version}"],
                Div.Class("h-b").OnClick(() => CountB++)["b"],
                Div.Class("h-c").OnClick(() => CountC++)["c"]
            ];
    }

    [Fact]
    public async Task MultiHandlerComponent_KeepsEverySlotId_AcrossRenders_AndEachStillDispatches()
    {
        var three = new ThreeHandlers();
        var root = new StubComponent(() => Div.Class("page")[three]);

        var before = root.RenderAsLiveRoot();
        var (a, b, c) = (IdOn(before, "h-a"), IdOn(before, "h-b"), IdOn(before, "h-c"));
        Assert.Equal(3, new HashSet<string> { a, b, c }.Count);

        three.Touch();
        var after = root.RenderAsLiveRoot();

        Assert.Equal(a, IdOn(after, "h-a"));
        Assert.Equal(b, IdOn(after, "h-b"));
        Assert.Equal(c, IdOn(after, "h-c"));

        // Every slot resolves to ITS OWN delegate — the failure mode of a slot table that drifts is a
        // stable-looking id wired to the neighbouring handler.
        Assert.True(await root.TryInvokeHandlerAsync(a, EmptyPayload));
        Assert.True(await root.TryInvokeHandlerAsync(b, EmptyPayload));
        Assert.True(await root.TryInvokeHandlerAsync(c, EmptyPayload));
        Assert.Equal((1, 1, 1), (three.CountA, three.CountB, three.CountC));
    }

    // ---- Numbering follows the RENDERING component, not the delegate's target --------------------

    private sealed class Callee : Component
    {
        public int External;
        public int Own;

        public void OnExternal() => External++;

        protected override Component? Render() =>
            Div.Class("callee").OnClick(() => Own++)["callee"];
    }

    private sealed class Caller : Component
    {
        public Callee? Target;
        private bool _wired;

        public void Wire()
        {
            _wired = true;
            StateHasChanged();
        }

        protected override Component? Render() => _wired
            ? Div.Class("caller-wrap")[
                Div.Class("to-callee").OnClick(Target!.OnExternal)["call out"],
                Div.Class("caller").OnClick(() => { })["caller"]
            ]
            : Div.Class("caller-wrap")[Div.Class("caller").OnClick(() => { })["caller"]];
    }

    [Fact]
    public async Task HandlerTargetingAnotherComponent_DoesNotShiftThatComponentsOwnIds()
    {
        // The delegate's target is `callee`, but it is REGISTERED during `caller`'s render. Slot
        // numbering is anchored to the component whose Render() emitted the element (CurrentParent), so
        // this consumes one of the CALLER's slots — a callee that renders nothing new keeps its own id.
        // Anchoring to the delegate target instead would let a callback passed into a wrapper renumber
        // the component it was passed from.
        var callee = new Callee();
        var caller = new Caller { Target = callee };
        var root = new StubComponent(() => Div.Class("page")[caller, callee]);

        var before = root.RenderAsLiveRoot();
        var calleeId = IdOn(before, "callee");

        caller.Wire();
        var after = root.RenderAsLiveRoot();

        Assert.Equal(calleeId, IdOn(after, "callee"));

        // The cross-component handler fires on its own id, and dirty-marking still follows the delegate
        // target (Callee), not the slot anchor — the two roles stay separate.
        var crossId = IdOn(after, "to-callee");
        Assert.NotEqual(calleeId, crossId);
        Assert.True(await root.TryInvokeHandlerAsync(crossId, EmptyPayload));
        Assert.Equal(1, callee.External);
        Assert.Equal(0, callee.Own);

        Assert.True(await root.TryInvokeHandlerAsync(calleeId, EmptyPayload));
        Assert.Equal(1, callee.Own);
    }

    // ---- A number is never handed to a second component --------------------------------------------

    private sealed class LeafA : Component
    {
        public int Clicks;

        protected override Component? Render() => Div.Class("leaf-a").OnClick(() => Clicks++)["a"];
    }

    private sealed class LeafB : Component
    {
        public int Clicks;

        protected override Component? Render() => Div.Class("leaf-b").OnClick(() => Clicks++)["b"];
    }

    [Fact]
    public async Task UnmountedHandlerId_NoOps_AndIsNotReusedByTheComponentThatReplacesIt()
    {
        // Recycling a freed number is what makes a stale in-flight event dangerous: the click a user
        // sent a moment before the row vanished would resolve to whatever took the number. Numbers are
        // therefore issued once and never reissued — the stale id resolves to nothing and no-ops.
        var showA = true;
        var leafA = new LeafA();
        var leafB = new LeafB();
        var root = new StubComponent(() => showA
            ? Div.Class("page")[leafA]
            : Div.Class("page")[leafB]);

        var before = root.RenderAsLiveRoot();
        var idA = IdOn(before, "leaf-a");

        showA = false;
        var after = root.RenderAsLiveRoot();
        var idB = IdOn(after, "leaf-b");

        Assert.NotEqual(idA, idB);
        Assert.False(await root.TryInvokeHandlerAsync(idA, EmptyPayload), "a departed id must not resolve");
        Assert.Equal(0, leafA.Clicks);
        Assert.Equal(0, leafB.Clicks);

        Assert.True(await root.TryInvokeHandlerAsync(idB, EmptyPayload));
        Assert.Equal(1, leafB.Clicks);
    }

    private sealed class Row : Component
    {
        public int Clicks;
        public int Index;

        protected override Component? Render() =>
            Div.Class($"row-{Index}").OnClick(() => Clicks++)[$"row {Index}"];
    }

    [Fact]
    public void GrowingList_KeepsEveryExistingRowsId_AndDrawsAFreshNumberForEachNewRow()
    {
        // The payload win in one test. A list that grows by one used to renumber every row after the
        // insertion point, so the diff rewrote data-rask-on-click on rows whose markup was identical.
        // Now an existing row's id is settled for its lifetime and only the new row draws a number.
        // Held across renders, the way a generated factory's GetOrCreate hands back the same persisted
        // instance for a given list position — constructing fresh rows every render would instead be
        // testing a page that remounts its whole list, which has no ids to keep.
        var rows = new List<Component>();
        var root = new StubComponent(() => Div.Class("page")[rows]);

        var settled = new Dictionary<int, string>();
        var issued = new HashSet<int>();

        for (var count = 3; count <= 8; count++)
        {
            while (rows.Count < count)
            {
                rows.Add(new Row { Index = rows.Count });
            }

            var html = root.RenderAsLiveRoot();
            for (var i = 0; i < count; i++)
            {
                var id = IdOn(html, $"row-{i}");
                if (settled.TryGetValue(i, out var previous))
                {
                    Assert.Equal(previous, id);
                    continue;
                }

                Assert.True(issued.Add(NumberOf(id)), $"row {i} was handed the reissued number {id}");
                settled[i] = id;
            }
        }

        Assert.Equal(8, settled.Count);
    }

    [Fact]
    public async Task ComponentRenderedUnderASecondRoot_DoesNotCollideWithThatRootsOwnIds()
    {
        // A slot id is minted from whichever root was rendering and then cached on the component. If a
        // component is later reached from a DIFFERENT root, handing back the id minted under the first
        // would hand the second root an id it never issued — and the number it does issue next then
        // collides, wiring two elements to one entry in the map. Reachable through ordinary API: a
        // second Render of the same instance builds a fresh root. So a component that changes root
        // re-mints its slots from the new root's sequence.
        var shared = new Steady();
        var firstRoot = new StubComponent(() => Div.Class("page")[shared]);
        var firstHtml = firstRoot.RenderAsLiveRoot();
        var idUnderFirst = IdOn(firstHtml, "steady");

        // The second root renders a handler of its own BEFORE the shared component, so if the shared
        // one keeps its old id the two collide on the number the new root issues first.
        var native = new Steady();
        var secondRoot = new StubComponent(() => Div.Class("page")[native, shared]);
        var secondHtml = secondRoot.RenderAsLiveRoot();

        var nativeId = IdOn(secondHtml, "steady");
        var sharedId = Regex.Matches(secondHtml, "class=\"steady\"[^>]*?data-rask-on-[a-z]+=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).Last();

        Assert.NotEqual(nativeId, sharedId);
        Assert.True(await secondRoot.TryInvokeHandlerAsync(sharedId, EmptyPayload));
        Assert.Equal(1, shared.Clicks);
        Assert.Equal(0, native.Clicks);

        Assert.True(await secondRoot.TryInvokeHandlerAsync(nativeId, EmptyPayload));
        Assert.Equal(1, native.Clicks);
        Assert.Equal(1, shared.Clicks);
        _ = idUnderFirst;
    }

    [Fact]
    public void FirstRenderStillNumbersInWalkOrder_FromZero()
    {
        // The scheme draws a number the first time it reaches a slot, and the first render reaches
        // them in walk order — so an initial page is byte-identical to what the positional counter
        // produced. This is what keeps the change invisible to the client and to every existing
        // expected-HTML test.
        var root = new StubComponent(() => Div.Class("page")[
            Div.Class("one").OnClick(() => { })["1"],
            Div.Class("two").OnClick(() => { })["2"],
            Div.Class("three").OnClick(() => { })["3"]
        ]);

        var html = root.RenderAsLiveRoot();

        Assert.Equal("h0", IdOn(html, "one"));
        Assert.Equal("h1", IdOn(html, "two"));
        Assert.Equal("h2", IdOn(html, "three"));
    }
}
