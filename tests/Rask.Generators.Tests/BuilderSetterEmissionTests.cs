namespace Rask.Generators.Tests;

// PROTOTYPE — the builder setters carry the propsChanged fold the generated factory computes in one
// shot. The factory can snapshot every prop, assign them all and diff once, because it knows where the
// assignments end; a setter chain does not, so each FOLDING setter accumulates its own delta and the
// parent fires the single NotifyParameters when its Render() returns.
//
// Which props fold has to match the factory exactly (see EmitFactory's foldProps), so these pin both
// halves: a value prop tracks, and Key / delegates / auto-wrapped callbacks do not — folding a fresh
// closure every render would report a change every frame and defeat the render cache outright.
public class BuilderSetterEmissionTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;
                               public partial class Widget : Component
                               {
                                   public string? Title { get; set; }
                                   public int Count { get; set; }
                                   public System.Action? OnPick { get; set; }
                               }
                               """;

    [Fact]
    public void A_value_setter_folds_into_props_changed()
    {
        var output = Run(Src);

        Assert.Contains(
            "Title(this global::Demo.Widget __c, string? value) "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Title, value); "
            + "global::Rask.Core.BuilderRuntime.Written(__c, 0x10000UL); __c.Title = value; return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.BuilderRuntime.Track(__c, __c.Count, value);", output,
            StringComparison.Ordinal);
    }

    // The reset half. A folding setter also clears its pending bit, because the entry marks every
    // folding prop pending and whatever is still pending when the parent's Render() returns is put
    // back to the factory's default — that is what stops `Widget.Title("x")` on one render and
    // `Widget` on the next from keeping the title. Own props are numbered from OwnPendingBit (16) up,
    // above the range the shared Element/Component surface reserves for itself.
    [Fact]
    public void Own_props_claim_pending_bits_above_the_shared_surface()
    {
        var output = Run(Src);

        Assert.Contains("global::Rask.Core.BuilderRuntime.Written(__c, 0x10000UL);", output,
            StringComparison.Ordinal);
        Assert.Contains("public static void __RaskResetPending_Demo_Widget(", output, StringComparison.Ordinal);
        Assert.Contains("__c.Title, null))", output, StringComparison.Ordinal);

        // `int Count` is non-nullable with no initializer — a REQUIRED factory parameter. The factory
        // re-applies it from the caller's argument every render; a chain that stops naming it has
        // nothing to re-apply, so it claims a bit and resets like any other folding prop, to `default!`.
        var count = output.Split('\n').Single(l => l.Contains(" Count(this ", StringComparison.Ordinal));
        Assert.Contains("BuilderRuntime.Written(__c, 0x20000UL)", count, StringComparison.Ordinal);
        Assert.Contains("__c.Count, default!)", output, StringComparison.Ordinal);
        Assert.Contains("__c.Count = default!;", output, StringComparison.Ordinal);
    }

    // A non-folding prop is defaulted the moment the entry is created instead: it never calls Track, so
    // blanking it early cannot make an unchanged component look dirty.
    [Fact]
    public void A_callback_prop_is_reset_eagerly_rather_than_claiming_a_bit()
    {
        var output = Run(Src);

        var pick = output.Split('\n').Single(l => l.Contains(" Pick(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Written", pick, StringComparison.Ordinal);
        Assert.Contains("__c.OnPick = null;", EagerReset(output, "Widget"), StringComparison.Ordinal);
    }

    // A prop with a constant member initializer is an OPTIONAL factory param whose default is that
    // value, so the reset restores the initializer — resetting it to null would be a different bug.
    [Fact]
    public void A_prop_with_a_member_initializer_resets_to_the_initializer()
    {
        var output = Run("""
                         using Rask.Core;
                         namespace Demo;
                         public partial class Widget : Component
                         {
                             public string Note { get; set; } = "n/a";
                             public System.Collections.Generic.List<string> Rows { get; set; } = new();
                         }
                         """);

        // Note folds, so it is reset at the END of the render, and against the initializer — never null.
        var pending = PendingReset(output, "Widget");
        Assert.Contains("Equals(__c.Note, \"n/a\")", pending, StringComparison.Ordinal);
        Assert.Contains("__c.Note = \"n/a\";", pending, StringComparison.Ordinal);

        // A NON-constant initializer excludes the prop from the factory parameters entirely, so the
        // factory can neither set it nor put it back — and neither may the builder. It used to get a
        // setter and no reset, which is the staleness bug the deferred reset exists to prevent, just
        // pointed the other way: written once through a chain, the value survived every later render
        // with nothing able to clear it. One predicate answers both questions now, so a prop with no
        // reset has no setter either.
        Assert.DoesNotContain("__c.Rows", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("__c.Rows", EagerReset(output, "Widget"), StringComparison.Ordinal);
        Assert.DoesNotContain(" Rows(this ", output, StringComparison.Ordinal);
    }

    // A reset is named after the component's FULL name — the simple one is not unique across namespaces.
    private static string EagerReset(string output, string type) =>
        BuilderGeneratorHarness.Method(output, "__RaskResetEager_Demo_" + type);

    private static string PendingReset(string output, string type) =>
        BuilderGeneratorHarness.Method(output, "__RaskResetPending_Demo_" + type);

    // A callback prop is a fresh delegate (and, once wrapped, a fresh closure) on every render, so it
    // is never meaningfully equal to the previous one. The factory leaves it out of the fold; so does
    // the setter, or every callback-taking component would re-render every frame.
    [Fact]
    public void A_callback_setter_does_not_fold()
    {
        var output = Run(Src);

        var line = output.Split('\n').Single(l => l.Contains(" Pick(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Key is a reconciliation identity, not a reactive prop: a changed Key means a different logical
    // item, which mounts fresh rather than firing OnPropsChanged on the old instance.
    [Fact]
    public void The_Key_setter_does_not_fold()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public partial class Widget : Component
                  {
                      public object? Key { get; set; }
                  }
                  """;

        var line = Run(src).Split('\n').Single(l => l.Contains(" Key(this ", StringComparison.Ordinal));

        Assert.DoesNotContain("BuilderRuntime.Track", line, StringComparison.Ordinal);
    }

    // Element's ~93 universal props are emitted ONCE as constrained generic extensions rather than
    // per tag, so they take a different branch of EmitSetter. Only the assembly declaring Element
    // contributes them — hence the source-declared Element here.
    [Fact]
    public void The_shared_generic_setters_fold_the_same_way()
    {
        var src = """
                  namespace Rask.Core;
                  public abstract partial class Element : Component
                  {
                      public string? Class { get; set; }
                      public global::Rask.Core.Callback? OnClick { get; set; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains(
            "public static T Class<T>(this T __c, string? value) where T : global::Rask.Core.Element "
            + "{ global::Rask.Core.BuilderRuntime.Track(__c, __c.Class, value); "
            + "global::Rask.Core.BuilderRuntime.Written(__c, 0x1UL); __c.Class = value; return __c; }",
            output,
            StringComparison.Ordinal);

        var click = output.Split('\n').Single(l => l.Contains(" Click<T>(this ", StringComparison.Ordinal));
        Assert.DoesNotContain("BuilderRuntime.Track", click, StringComparison.Ordinal);
    }

    // A carrier prop keeps its own name — that is the whole point of the carrier, and it is what makes
    // Element's event surface read `.OnClick(…)` instead of `.Click(…)`. The SETTER still takes the
    // delegate: a method group cannot reach a carrier, because C# will not chain a delegate conversion
    // into a user-defined one. And it must not be AutoCallback-wrapped — a DOM handler is forwarded
    // raw, where handler-owner resolution already re-renders the owner.
    [Fact]
    public void A_carrier_element_event_keeps_its_name_and_is_not_wrapped()
    {
        var src = """
                  namespace Rask.Core;
                  public abstract partial class Element : Component
                  {
                      public global::Rask.Core.Handler? OnClick { get; set; }
                      public global::Rask.Core.Handler<global::Rask.Core.Live.MouseEventArgs>? OnMouseDown { get; set; }
                      public global::Rask.Core.HandlerAsync? OnClickAsync { get; set; }
                  }
                  """;

        var output = Run(src);

        Assert.Contains(
            "public static T OnClick<T>(this T __c, global::Rask.Core.Callback? value) "
            + "where T : global::Rask.Core.Element "
            + "{ __c.OnClick = global::Rask.Core.Handler.From(value); return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnMouseDown<T>(this T __c, "
            + "global::Rask.Core.Callback<global::Rask.Core.Live.MouseEventArgs>? value)",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnClickAsync<T>(this T __c, global::Rask.Core.CallbackAsync? value)",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCallback.Wrap", output, StringComparison.Ordinal);
    }

    // The other half of the carrier rule, and the one that fails SILENTLY. A carrier on a
    // non-Element component (BsButton.OnClick, BsDataGrid.OnSortChange, Input's DragDrop sibling…)
    // must STAY AutoCallback-wrapped: a component callback has no DOM handler-owner resolution behind
    // it, so dropping the wrapper stops the consumer re-rendering while the markup stays identical.
    // IsAutoRerenderProp answers the question through the carrier; this pins that it still does.
    [Fact]
    public void A_carrier_callback_on_a_non_element_component_is_still_auto_wrapped()
    {
        var output = Run("""
                         using Rask.Core;
                         namespace Demo;
                         public partial class Widget : Component
                         {
                             public Handler? OnPick { get; set; }
                             public HandlerAsync<string>? OnPickAsync { get; set; }
                             public Carrier<System.Action<int>>? OnRank { get; set; }
                         }
                         """);

        Assert.Contains(
            "OnPick(this global::Demo.Widget __c, global::Rask.Core.Callback? value) "
            + "{ __c.OnPick = global::Rask.Core.Handler.From(global::Rask.Core.AutoCallback.Wrap(value)); "
            + "return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__c.OnPickAsync = global::Rask.Core.HandlerAsync<string>"
            + ".From(global::Rask.Core.AutoCallback.Wrap(value));",
            output,
            StringComparison.Ordinal);

        // Carrier<TDelegate> asks the same question of the delegate it carries, so an Action<int>
        // callback is wrapped for the same reason.
        Assert.Contains(
            "__c.OnRank = global::Rask.Core.Carrier<global::System.Action<int>>"
            + ".From(global::Rask.Core.AutoCallback.Wrap(value));",
            output,
            StringComparison.Ordinal);
    }

    // `From`, never `new Handler(value)` and never the bare implicit conversion: the conversion accepts
    // a null delegate, so an omitted argument would land as a non-null carrier wrapping null and the
    // component's own `OnClose is not null` tests would all start answering true. Pinned on the FACTORY
    // too, which is where an omitted argument actually arrives.
    [Fact]
    public void A_carrier_assignment_maps_a_null_delegate_to_an_unset_carrier()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public partial class Widget : Component
                  {
                      public Handler? OnPick { get; set; }
                  }
                  """;

        var factory = Run(src, "Demo.Generated.g.cs");

        Assert.Contains("global::Rask.Core.Callback? OnPick = null", factory, StringComparison.Ordinal);
        Assert.Contains(
            "__c.OnPick = global::Rask.Core.Handler.From(global::Rask.Core.AutoCallback.Wrap(OnPick));",
            factory,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__c.OnPick = new global::Rask.Core.Handler(", factory, StringComparison.Ordinal);
    }

    // A prop inherited from an INTERMEDIATE base (HtmlMediaElement, BsBlock, BsFormControl<T>, a
    // consumer's own base) has no shared emission to fall back on — only Rask.Core's Element/Component
    // chain is written once as constrained generics — so it needs a per-component setter or it gets
    // none at all. The receiver is the concrete component, so the chain keeps its type.
    [Fact]
    public void A_prop_from_an_intermediate_base_gets_a_setter_on_the_concrete_component()
    {
        var output = Run("""
                         using Rask.Core;
                         namespace Demo;
                         public abstract class Panel : Component
                         {
                             public string? Heading { get; set; }
                         }

                         public partial class Widget : Panel
                         {
                             public int Count { get; set; }
                         }
                         """);

        Assert.Contains("Heading(this global::Demo.Widget __c, string? value)", output, StringComparison.Ordinal);

        // …and the inherited prop is reset like an own one, or the chain would keep last render's
        // heading where the factory would have put it back.
        Assert.Contains("__c.Heading, null))", PendingReset(output, "Widget"), StringComparison.Ordinal);

        // Component's own props stay on the shared surface: emitting them here as well would be dead
        // weight on every component in the assembly.
        Assert.DoesNotContain("Key(this global::Demo.Widget", output, StringComparison.Ordinal);
    }

    // The shared Element/Component surface gets its reset emitted ONCE, into Rask.Core.BuilderRuntime —
    // a fixed name, because a consumer assembly cannot know the per-assembly setter class Rask.Core
    // emitted its own setters into. Only the assembly declaring Element contributes it.
    [Fact]
    public void The_shared_surface_reset_is_emitted_once_onto_BuilderRuntime()
    {
        var output = Run("""
                         namespace Rask.Core;
                         public abstract partial class Element : Component
                         {
                             public string? Class { get; set; }
                             public global::Rask.Core.Callback? OnClick { get; set; }
                         }
                         """, "RaskBuilderReset.g.cs");

        Assert.Contains("public static partial class BuilderRuntime", output, StringComparison.Ordinal);
        Assert.Contains("public static void ResetElementEager(global::Rask.Core.Component __c0)", output,
            StringComparison.Ordinal);
        Assert.Contains("__c.OnClick = null;", output, StringComparison.Ordinal);
        Assert.Contains("public const ulong SharedElementPending = 0x1UL;", output, StringComparison.Ordinal);

        // Key is Component's, not Element's, and never folds — so it is reset eagerly one level up.
        Assert.Contains("__c.Key = null;", output, StringComparison.Ordinal);
        Assert.Contains("public const ulong SharedComponentPending = 0x0UL;", output, StringComparison.Ordinal);
    }

    // A raw delegate property IS invocable, so C#'s invocable-member rule binds `grid.RowClass(fn)` to
    // the property and never looks at the same-named extension. The setter was emitted anyway: dead
    // code that reads like a working surface, and the prop simply cannot be set from a chain. It has to
    // move to a carrier (which is not invocable) — that is what a65abd3e/8587f44a did for every prop
    // named `On…`, and the props the `On` rule never touched were left behind.
    [Fact]
    public void A_delegate_prop_whose_setter_would_share_its_name_is_reported_not_emitted()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Widget : Component
                                              {
                                                  public System.Func<int, string>? Format { get; set; }
                                                  public Carrier<System.Func<int, string>>? Shape { get; set; }
                                              }
                                              """);

        var reported = Assert.Single(run.WithId("RASK042"));
        Assert.Contains("Demo.Widget.Format", reported.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Carrier<global::System.Func<int, string>>?", reported.GetMessage(),
            StringComparison.Ordinal);

        var output = run.Source("RaskBuilderSetters.g.cs");
        Assert.DoesNotContain(" Format(this ", output, StringComparison.Ordinal);

        // The carrier-typed sibling is the fix, so it keeps its name AND takes the raw delegate.
        Assert.Contains(
            "Shape(this global::Demo.Widget __c, global::System.Func<int, string>? value) "
            + "{ __c.Shape = global::Rask.Core.Carrier<global::System.Func<int, string>>.From(value); "
            + "return __c; }",
            output,
            StringComparison.Ordinal);
    }

    // …but not for a REQUIRED delegate prop (`required Func<…> Template`). Its component is excluded
    // from the entries entirely, so there is no chain for a setter to sit in, and its factory assigns
    // the prop on every render. Moving it to a carrier would only cost its non-nullness.
    [Fact]
    public void A_required_delegate_prop_is_not_reported()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Widget : Component
                                              {
                                                  public required System.Func<int, string> Format { get; set; }
                                              }
                                              """);

        Assert.Empty(run.WithId("RASK042"));
        Assert.DoesNotContain(" Format(this ", run.Source("RaskBuilderSetters.g.cs"), StringComparison.Ordinal);
    }

    // The bound IFormControl<T> members are emitted from the INTERFACE's types rather than from the
    // control's own declarations, and those types are carriers. Spelling them as the bare delegate here
    // made the assignment run the carrier's implicit conversion instead of `From`, so a null validator
    // or post-bind hook landed as a non-null carrier wrapping null — the trap `From` exists to close,
    // reopened one layer up, where `Validator`'s `Validate?.Fn ?? ValidateAsync?.Fn` reads it back.
    [Fact]
    public void The_bound_setters_assign_through_the_carriers_From()
    {
        var output = Run("""
                         using System;
                         using System.Linq.Expressions;
                         using System.Threading.Tasks;
                         using Rask.Core;
                         using Rask.Core.Forms;
                         namespace Demo;
                         public partial class Widget : Component, IFormControl<int>
                         {
                             public int? Value { get; set; }
                             public Handler<int>? OnChange { get; set; }
                             public HandlerAsync<int>? OnChangeAsync { get; set; }
                             public Expression<Func<int>>? Bind { get; set; }
                             public Carrier<Validate<int>>? Validate { get; set; }
                             public Carrier<ValidateAsync<int>>? ValidateAsync { get; set; }
                             public Carrier<Action<int>>? AfterBind { get; set; }
                             public Carrier<Func<int, Task>>? AfterBindAsync { get; set; }
                         }
                         """);

        // The parameter is still the plain delegate — a lambda or method group cannot reach a carrier.
        Assert.Contains(
            "Validate(this global::Demo.Widget __c, global::Rask.Core.Forms.Validate<int>? value) "
            + "{ __c.Validate = global::Rask.Core.Carrier<global::Rask.Core.Forms.Validate<int>>.From(value); "
            + "return __c; }",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__c.AfterBind = global::Rask.Core.Carrier<global::System.Action<int>>.From(value);",
            output,
            StringComparison.Ordinal);
    }

    // The pending-bit budget is fixed at 16 and handed out in ORDINAL NAME ORDER, so adding one folding
    // prop to Element does not push itself off the end — it pushes whichever alphabetically-later prop
    // was last. That one falls back to the eager reset, which reports it changed on every render and
    // defeats the render cache for it: no compile error, no failing test, just a slower framework.
    [Fact]
    public void Overflowing_the_shared_pending_bits_is_reported()
    {
        var props = string.Join("\n    ",
            Enumerable.Range(0, 17).Select(i => $"public string? P{i:00} {{ get; set; }}"));

        var run = BuilderGeneratorHarness.Run($$"""
                                               namespace Rask.Core;
                                               public abstract partial class Element : Component
                                               {
                                                   {{props}}
                                               }
                                               """);

        var reported = Assert.Single(run.WithId("RASK041"));
        Assert.Contains("17 folding properties but only 16 pending bits", reported.GetMessage(),
            StringComparison.Ordinal);
        Assert.Contains("'P16'", reported.GetMessage(), StringComparison.Ordinal);

        // …and the prop that fell off is reset eagerly rather than not at all.
        Assert.Contains("__c.P16 = null;", run.Source("RaskBuilderReset.g.cs"), StringComparison.Ordinal);
    }

    // Sixteen fits exactly, so the guard reports the overflow rather than the last legal prop.
    [Fact]
    public void A_full_but_not_overflowing_shared_surface_is_silent()
    {
        var props = string.Join("\n    ",
            Enumerable.Range(0, 16).Select(i => $"public string? P{i:00} {{ get; set; }}"));

        var run = BuilderGeneratorHarness.Run($$"""
                                               namespace Rask.Core;
                                               public abstract partial class Element : Component
                                               {
                                                   {{props}}
                                               }
                                               """);

        Assert.Empty(run.WithId("RASK041"));
        Assert.Contains("public const ulong SharedElementPending = 0xFFFFUL;", run.Source("RaskBuilderReset.g.cs"),
            StringComparison.Ordinal);
    }

    private static string Run(string source, string hintName = "RaskBuilderSetters.g.cs") =>
        BuilderGeneratorHarness.Run(source).Source(hintName);
}
