using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// A change on an Element-derived form control (Select/Input/Textarea) must re-render the CONSUMER
// that owns the affected state, not just the control.
//
// Controlled mode (OnChange + parent-owned state, no Bind) regressed: the control wraps the typed
// callback in its own DOM handler (to parse the raw string → T), so the handler's Target is the
// control and RegisterHandler's owner heuristic dirty-marked the control — never the component whose
// state OnChange mutates. IFormControl<T>.ControlledChangeHandler now notifies the callback's owning
// consumer after invoking it. Bound mode (two-way Bind) never had the bug: its handler is a
// BindingHelpers closure whose Target is not a Component, so the owner stays the consumer that
// rendered the control. Both are pinned here.
public class FormControlChangeRerenderTests
{
    [Fact]
    public async Task ControlledSelect_OnChange_RerendersConsumer_NotJustSelect()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new Host();

        var html = host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Picker.RenderCount);
        Assert.Contains("Picked: rask", html);

        var changeId = Markup.Attr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"blazor\"}");
        var ok = await host.TryInvokeHandlerAsync(changeId!, doc.RootElement);
        Assert.True(ok);

        // Picker has stable props, so a second render keeps it cached UNLESS the controlled
        // OnChange dirtied it. The regression: the dirty-mark landed on the Select, leaving Picker
        // cached at RenderCount 1 with stale "Picked: rask".
        var updated = host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Picker.RenderCount);
        Assert.Contains("Picked: blazor", updated);
    }

    [Fact]
    public async Task ControlledInput_OnChange_RerendersConsumer()
    {
        // Input<T> shares ControlledChangeHandler with Select/Textarea — the same fix covers it.
        var sp = RenderHarness.EmptyServices();
        var host = new InputHost();

        var html = host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Echo.RenderCount);
        Assert.Contains("Echo: a", html);

        var changeId = Markup.Attr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"z\"}");
        Assert.True(await host.TryInvokeHandlerAsync(changeId!, doc.RootElement));

        var updated = host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Echo.RenderCount);
        Assert.Contains("Echo: z", updated);
    }

    [Fact]
    public async Task ControlledInput_OnChange_CapturingALocal_StillRerendersConsumer()
    {
        // The closure case the original fix missed. `OnChange: v => _names[i] = v` inside a loop captures a
        // local ALONGSIDE `this`, so Roslyn lowers it to a display class and the delegate's Target is that
        // closure — not the component. The old `Target as Component` heuristic returned null, so nothing was
        // notified and the consumer stayed render-cached with stale text, silently.
        //
        // ControlledChangeHandler now resolves the consumer through DelegateOwner, the same unwrap-the-
        // captured-`this` rule RegisterHandler and AutoCallback already used. Rendering a list of controlled
        // inputs is an ordinary thing to do (a data grid's per-row checkbox is exactly this shape).
        var sp = RenderHarness.EmptyServices();
        var host = new ListHost();

        var html = host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Rows.RenderCount);
        Assert.Contains("Names: a,b", html);

        // The second row's input — proving the captured index, not just `this`, survives.
        var ids = System.Text.RegularExpressions.Regex.Matches(html, "data-rask-on-change=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(2, ids.Length);

        using var doc = JsonDocument.Parse("{\"value\":\"z\"}");
        Assert.True(await host.TryInvokeHandlerAsync(ids[1], doc.RootElement));

        var updated = host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Rows.RenderCount);
        Assert.Contains("Names: a,z", updated);
    }

    [Fact]
    public async Task BoundSelect_Change_RerendersConsumer()
    {
        // Two-way Bind: the change handler is a BindingHelpers closure (Target is not a Component),
        // so the owner stays the consumer that rendered the control — the model-derived text updates.
        var sp = RenderHarness.EmptyServices();
        var host = new BoundHost();

        var html = host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Form.RenderCount);
        Assert.Contains("Bound: red", html);

        var changeId = Markup.Attr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"blue\"}");
        Assert.True(await host.TryInvokeHandlerAsync(changeId!, doc.RootElement));

        var updated = host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Form.RenderCount);
        Assert.Contains("Bound: blue", updated);
    }

    [Fact]
    public async Task BoundChange_RerendersBindExpressionOwner_EvenWhenAnotherComponentRendersTheControl()
    {
        // The control is rendered by a CHILD wrapper, so the DOM handler's owner is the wrapper — not the
        // consumer that authored `() => _model.Name` and shows a derived readout outside the wrapper. The
        // framework records the bind expression's owning component on the EditContext and re-renders it on
        // change, so the readout updates with no StateHasChanged on the consumer. (This is the path the
        // sample Component-style controls — BsRadioGroup/BsCheckboxGroup/BsMultiSelect — rely on.)
        var sp = RenderHarness.EmptyServices();
        var host = new BindOwnerHost();

        var html = host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Consumer.RenderCount);
        Assert.Contains("Name: rask", html);

        var inputId = Markup.Attr(html, "data-rask-on-input");
        Assert.NotNull(inputId);

        using var doc = JsonDocument.Parse("{\"value\":\"neo\"}");
        Assert.True(await host.TryInvokeHandlerAsync(inputId!, doc.RootElement));

        var updated = host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Consumer.RenderCount); // consumer re-rendered via the binding owner
        Assert.Contains("Name: neo", updated);
    }

    // Root holds a render-cached intermediate (the consumer) so the bug — failing to dirty the
    // consumer — is observable: a fresh root render alone would mask it.
    private sealed class Host : Component
    {
        public readonly Picker Picker = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var p = ctx.GetOrCreate(_ => Picker);
            ctx.NotifyParameters(p, false); // stable props ⇒ Picker caches after first render
            return Div()[p];
        }
    }

    // OnChange is created inside Picker.Render so the lambda captures `this` (Target == the
    // consumer) — the shape EventsSelectDemo and real consumers use.
    private sealed class Picker : Component
    {
        private string _pick = "rask";
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Div()[
                Select<string>(OnChange: v => _pick = v)[
                    Option("rask"), Option("blazor")
                ],
                Span()["Picked: ", _pick]
            ];
        }
    }

    private sealed class InputHost : Component
    {
        public readonly Echo Echo = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var e = ctx.GetOrCreate(_ => Echo);
            ctx.NotifyParameters(e, false);
            return Div()[e];
        }
    }

    private sealed class Echo : Component
    {
        private string _text = "a";
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Div()[
                Input<string>(OnChange: v => _text = v),
                Span()["Echo: ", _text]
            ];
        }
    }

    private sealed class ListHost : Component
    {
        public readonly NameRows Rows = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var r = ctx.GetOrCreate(_ => Rows);
            ctx.NotifyParameters(r, false); // stable props ⇒ cached unless the change dirties it
            return Div()[r];
        }
    }

    // Two things have to be true at once for the bug to bite, and both are ordinary:
    //
    //   1. Each OnChange captures the loop index ALONGSIDE `this`, so Roslyn lowers it to a display class
    //      and the delegate's Target is that closure rather than this component.
    //   2. The controls are built here but handed as CHILDREN to a wrapper, so the element's render-owner
    //      is the wrapper, not this component.
    //
    // Without (2) the fallback owner (the rendering component) happens to be the consumer anyway and the
    // bug is masked — which is exactly why this test wraps. It is the shape every composite table/list
    // component produces: BsDataGrid builds its cells and passes them into BsTable.
    private sealed class NameRows : Component
    {
        private readonly string[] _names = ["a", "b"];
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Div()[
                new Wrapper { Body = [.. _names.Select((n, i) => Input<string>(Value: n, OnChange: v => _names[i] = v, Key: i))] },
                Span()["Names: ", string.Join(",", _names)]
            ];
        }
    }

    // Renders someone else's elements inside its own subtree, so CurrentParent — the fallback handler owner
    // — is this wrapper and never the component whose state the handler mutates.
    private sealed class Wrapper : Component
    {
        public IReadOnlyList<Component> Body { get; set; } = [];

        protected override Component? Render() => Div()[Body];
    }

    private sealed class BoundHost : Component
    {
        public readonly BoundForm Form = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var f = ctx.GetOrCreate(_ => Form);
            ctx.NotifyParameters(f, false);
            return Div()[f];
        }
    }

    private sealed class BoundForm : Component
    {
        private readonly Model _model = new() { Color = "red" };
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Form(_model)[
                Select(() => _model.Color)[Option("red"), Option("blue")],
                Span()["Bound: ", _model.Color ?? ""]
            ];
        }

        private sealed class Model
        {
            public string? Color { get; set; }
        }
    }

    private sealed class BindOwnerHost : Component
    {
        public readonly BindOwnerConsumer Consumer = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Consumer);
            ctx.NotifyParameters(c, false);
            return Div()[c];
        }
    }

    // Authors the bind expression (() => _model.Name) and the derived readout, but delegates rendering of
    // the actual control to a child wrapper — so the DOM handler owner is the wrapper, not this consumer.
    private sealed class BindOwnerConsumer : Component
    {
        private readonly Model _model = new() { Name = "rask" };
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Form(_model)[
                new BindWrapper { Bind = () => _model.Name },
                Span()["Name: ", _model.Name]
            ];
        }

        private sealed class Model
        {
            public string Name { get; set; } = "";
        }
    }

    // A child component that renders the bound control from a forwarded expression. The control's handler
    // is therefore owned by THIS wrapper, not by the consumer that authored the expression.
    private sealed class BindWrapper : Component
    {
        public System.Linq.Expressions.Expression<Func<string>>? Bind { get; set; }

        protected override Component? Render() => Input(Bind!);
    }
}
