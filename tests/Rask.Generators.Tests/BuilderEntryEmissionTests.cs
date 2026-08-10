namespace Rask.Generators.Tests;

// PROTOTYPE — the builder ENTRY emission: which components get a `Foo[…]` entry at all, and the two
// ways that decision used to go wrong silently.
//
// An entry is a single member named after its component type, hung on one type (Rask.Core.Component, or
// each consuming component's own partial). Factories are not: they live in a per-namespace `Generated`
// class. So everything a factory can express with a namespace, an entry has to express with a simple
// name — and every place that assumption leaks is a place where the generated code compiles and then
// does the wrong thing at render time.
public class BuilderEntryEmissionTests
{
    private const string Entries = "RaskBuilderConsumerEntries.g.cs";
    private const string Setters = "RaskBuilderSetters.g.cs";

    // Two components of the same simple name in different namespaces. Both have a factory
    // (Demo.Products.Generated.Card and Demo.Orders.Generated.Card); neither can have the entry,
    // because there is one `Card` member to hand out and nothing here can decide which type it means.
    // Silently dropping the second — which is what keying the emission by simple name did — leaves it
    // with no entry and, once the factory is deleted, no way to be built at all.
    [Fact]
    public void Two_components_with_the_same_simple_name_collide_loudly_and_neither_gets_an_entry()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo
                                              {
                                                  public partial class Page : Component { }
                                              }
                                              namespace Demo.Products
                                              {
                                                  public partial class Card : Component
                                                  {
                                                      public string? Note { get; set; }
                                                  }
                                              }
                                              namespace Demo.Orders
                                              {
                                                  public partial class Card : Component
                                                  {
                                                      public string? Other { get; set; }
                                                  }
                                              }
                                              """);

        var collisions = run.WithId("RASK040").ToList();
        Assert.Equal(2, collisions.Count);
        Assert.All(collisions, d =>
        {
            Assert.Contains("Demo.Products.Card", d.GetMessage(), StringComparison.Ordinal);
            Assert.Contains("Demo.Orders.Card", d.GetMessage(), StringComparison.Ordinal);
        });

        var entries = run.Source(Entries);
        Assert.DoesNotContain(" Card =>", entries, StringComparison.Ordinal);
    }

    // The bug the collision hid. The entry pass skipped a candidate BEFORE recording the name it had
    // taken (a `required` member gives it no valid no-argument entry), while the reset pass only asked
    // whether the candidate had anything to reset — so the two disagreed about which `Card` won. The
    // entry then pointed at a reset generated for the OTHER type, whose first statement is a cast:
    // `var __c = (Demo.Products.Card)__c0;` applied to a Demo.Orders.Card. An InvalidCastException at
    // render time, out of source that compiles clean.
    //
    // Both halves are keyed by the fully qualified name now, so each type gets its own reset and an
    // entry can only ever name its own.
    [Fact]
    public void An_entrys_reset_belongs_to_the_entrys_own_type()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo
                                              {
                                                  public partial class Page : Component { }
                                              }
                                              namespace Demo.Products
                                              {
                                                  public partial class Card : Component
                                                  {
                                                      public required System.Func<Component> Template { get; set; }
                                                      public string? Note { get; set; }
                                                  }
                                              }
                                              namespace Demo.Orders
                                              {
                                                  public partial class Card : Component
                                                  {
                                                      public string? Other { get; set; }
                                                  }
                                              }
                                              """);

        // Products.Card's required member is a raw DELEGATE, which no chain could ever set, so it is
        // still withheld an entry and only Orders.Card is eligible — no collision.
        Assert.Empty(run.WithId("RASK040"));

        // The canonical entry — the one place the reset triple is written — and the member injected into
        // every other component, which forwards to it. Both have to name the same `Card`.
        var entry = run.Source("RaskBuilderEntryHost.g.cs").Split('\n')
            .Single(l => l.Contains(" Card =>", StringComparison.Ordinal));
        Assert.Contains("Entry<global::Demo.Orders.Card>", entry, StringComparison.Ordinal);
        Assert.Contains("__RaskResetEager_Demo_Orders_Card", entry, StringComparison.Ordinal);
        Assert.Contains(
            "    private static global::Demo.Orders.Card Card => global::RaskEntriesTestAssembly.Card;",
            run.Source(Entries),
            StringComparison.Ordinal);

        // …and that reset casts to the type the entry builds, not to the other Card. (The cast rides on
        // the PENDING half here: both Cards' own props fold, so the eager half has nothing to write.)
        Assert.Contains("__RaskResetPending_Demo_Orders_Card", entry, StringComparison.Ordinal);
        var reset = BuilderGeneratorHarness.Method(run.Source(Setters), "__RaskResetPending_Demo_Orders_Card");
        Assert.Contains("(global::Demo.Orders.Card)__c0", reset, StringComparison.Ordinal);
        Assert.Contains("__c.Other", reset, StringComparison.Ordinal);

        // The other Card keeps its own reset rather than losing it to the name it shares.
        Assert.Contains("(global::Demo.Products.Card)__c0",
            BuilderGeneratorHarness.Method(run.Source(Setters), "__RaskResetPending_Demo_Products_Card"),
            StringComparison.Ordinal);
    }

    // Same-named GENERIC components are the one shape that survives a shared name: their entries are
    // methods, so different arities are overloads (BsSelect<TItem> next to BsSelect<TValue, TItem>).
    [Fact]
    public void Same_named_generic_form_controls_of_different_arity_both_get_an_entry()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using System;
                                              using System.Linq.Expressions;
                                              using System.Threading.Tasks;
                                              using Rask.Core;
                                              using Rask.Core.Forms;
                                              namespace Demo;
                                              public partial class Page : Component { }

                                              public partial class Pick<TItem> : Component, IFormControl<TItem>
                                              {
                                                  public TItem? Value { get; set; }
                                                  public Handler<TItem>? OnChange { get; set; }
                                                  public HandlerAsync<TItem>? OnChangeAsync { get; set; }
                                                  public Expression<Func<TItem>>? Bind { get; set; }
                                                  public Carrier<Validate<TItem>>? Validate { get; set; }
                                                  public Carrier<ValidateAsync<TItem>>? ValidateAsync { get; set; }
                                                  public Carrier<Action<TItem>>? AfterBind { get; set; }
                                                  public Carrier<Func<TItem, Task>>? AfterBindAsync { get; set; }
                                              }

                                              public partial class Pick<TValue, TItem> : Component, IFormControl<TValue>
                                              {
                                                  public TValue? Value { get; set; }
                                                  public Handler<TValue>? OnChange { get; set; }
                                                  public HandlerAsync<TValue>? OnChangeAsync { get; set; }
                                                  public Expression<Func<TValue>>? Bind { get; set; }
                                                  public Carrier<Validate<TValue>>? Validate { get; set; }
                                                  public Carrier<ValidateAsync<TValue>>? ValidateAsync { get; set; }
                                                  public Carrier<Action<TValue>>? AfterBind { get; set; }
                                                  public Carrier<Func<TValue, Task>>? AfterBindAsync { get; set; }
                                              }
                                              """);

        Assert.Empty(run.WithId("RASK040"));
        var entries = run.Source(Entries);
        Assert.Contains("Pick<TItem>(global::System.Linq.Expressions.Expression", entries, StringComparison.Ordinal);
        Assert.Contains("Pick<TValue, TItem>(global::System.Linq.Expressions.Expression", entries,
            StringComparison.Ordinal);
    }

    // A non-nullable prop with no member initializer is a REQUIRED factory parameter (RASK001): the
    // factory demands it on every call and re-assigns it on every render. That used to withhold the
    // entry, because an entry has no argument to carry the value and nothing was putting it back —
    // `Widget.Title("x")` on one render and a bare `Widget` on the next silently kept the title. Both
    // halves have an answer now (RASK038 at the call site, `default!` in the reset), so the component
    // gets its entry, and this pins that the reset comes with it: an entry without one is the silent
    // failure, and the two are emitted from different passes.
    [Fact]
    public void A_component_with_a_required_factory_param_gets_an_entry_and_a_reset_for_it()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Widget : Component
                                              {
                                                  public string Title { get; set; }
                                                  public string? Note { get; set; }
                                              }

                                              public partial class Optional : Component
                                              {
                                                  public string? Note { get; set; }
                                              }
                                              """);

        var entries = run.Source(Entries);
        Assert.Contains(" Widget =>", entries, StringComparison.Ordinal);
        Assert.Contains(" Optional =>", entries, StringComparison.Ordinal);

        // A folding prop's reset is deferred to the end of the parent's Render(), so it reads as a
        // pending-bit test rather than a bare assignment — `default!`, not `default`, because the
        // property is non-nullable by definition.
        var setters = run.Source(Setters);
        Assert.Contains("__c.Title = default!;", setters, StringComparison.Ordinal);
    }

    // A `required` MEMBER used to withhold the entry outright, because BuilderRuntime.Entry<T> is
    // constrained `where T : Component, new()` and a type with a required member does not satisfy
    // `new()` (CS9040). That is a construction problem with a construction answer: requiredness carries
    // no runtime enforcement, so EntryRequired<T> builds through Activator instead and drops the
    // constraint. What enforces the value is RASK038 on the chain.
    [Fact]
    public void A_component_with_a_required_member_is_built_through_Activator()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Widget : Component
                                              {
                                                  public required string Title { get; set; }
                                              }

                                              public partial class Optional : Component
                                              {
                                                  public string? Note { get; set; }
                                              }
                                              """);

        // The consumer forwarders say the entry exists at all; the host is where the construction path
        // is chosen, so that is where the two have to differ.
        Assert.Contains(" Widget =>", run.Source(Entries), StringComparison.Ordinal);

        var host = run.Source("RaskBuilderEntryHost.g.cs");
        Assert.Contains("EntryRequired<global::Demo.Widget>", host, StringComparison.Ordinal);

        // …and a component without a required member keeps the cheap `new T()` path, which is the whole
        // reason the reflective construction is a separate helper rather than the default.
        Assert.Contains("Entry<global::Demo.Optional>", host, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryRequired<global::Demo.Optional>", host, StringComparison.Ordinal);
    }

    // A generic component's entry has to be a METHOD, and its one argument is what pins the type
    // argument. That used to be available only to an IFormControl<T>, whose `Bind` expression carried
    // the value type — so `Form<TModel>`, the shape the migration actually needs next, would have got no
    // entry at all and would have left the builder surface the moment it became generic.
    //
    // The rule is now "the property that pins the type argument", and a form control's `Bind` is one way
    // of naming it rather than the only one. Same emission either way, which is the point: a second
    // shape would otherwise have meant a second helper and a second eligibility rule, and two
    // eligibility rules is exactly how the bound path drifted from the general one before.
    [Fact]
    public void A_generic_component_infers_its_type_argument_from_a_type_parameter_property()
    {
        var host = BuilderGeneratorHarness.Run("""
                                               using Rask.Core;
                                               namespace Demo;
                                               public partial class Holder<T> : Component
                                               {
                                                   public T? Item { get; set; }
                                                   public string? Note { get; set; }
                                               }
                                               """).Source("RaskBuilderEntryHost.g.cs");

        Assert.Contains("Holder<T>(T Item)", host, StringComparison.Ordinal);

        // …and it leaves the property exactly as the property's own setter would. Folding the change
        // keeps propsChanged honest; clearing the pending bit stops the deferred reset putting back the
        // value the entry just set, which would blank it on the very first render.
        Assert.Contains("BuilderRuntime.Track(__c, __c.Item, Item)", host, StringComparison.Ordinal);
        Assert.Contains("BuilderRuntime.Written(__c,", host, StringComparison.Ordinal);
        Assert.Contains("__c.Item = Item;", host, StringComparison.Ordinal);
    }

    // The other half of the same rule: a generic component with nothing that pins its type argument gets
    // no entry, because there would be no way to call it without writing the type argument by hand —
    // which is what the zero-argument overload is already for. BsDataGrid<T> is the real one (its props
    // are IEnumerable<T> and List<BsColumn<T>>, neither of which IS T), and this is what keeps it, and
    // every sample call site that builds one, exactly where it was.
    [Fact]
    public void A_generic_component_with_nothing_to_infer_from_gets_no_entry()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using System.Collections.Generic;
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Grid<T> : Component
                                              {
                                                  public IEnumerable<T>? Rows { get; set; }
                                              }

                                              public partial class Plain : Component
                                              {
                                                  public string? Note { get; set; }
                                              }
                                              """);

        var host = run.Source("RaskBuilderEntryHost.g.cs");
        Assert.DoesNotContain("Grid<T>", host, StringComparison.Ordinal);
        Assert.Contains(" Plain =>", host, StringComparison.Ordinal);
    }

    // The one shape a required member still blocks, and it is not about construction: a raw delegate
    // prop is INVOCABLE, so `x.Template(fn)` binds to the property and a same-named setter can never be
    // reached (the RASK042 rule). An optional prop of that shape moves to a carrier; a required one
    // cannot, because a carrier built from a null delegate is a non-null carrier wrapping null — exactly
    // the state `required` exists to forbid. An entry here would be constructible and never completable.
    [Fact]
    public void A_component_with_a_required_delegate_still_gets_no_entry()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using System;
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Templated : Component
                                                  {
                                                      public required Func<Component> Template { get; set; }
                                                  }

                                                  public partial class Optional : Component
                                                  {
                                                      public string? Note { get; set; }
                                                  }
                                                  """).Source(Entries);

        Assert.DoesNotContain(" Templated =>", entries, StringComparison.Ordinal);
        Assert.Contains(" Optional =>", entries, StringComparison.Ordinal);
    }

    // The syntax provider yields one candidate per class DECLARATION, so a partial class whose
    // declarations each carry a base list is seen twice. Emitting its setters (or its reset) twice is
    // CS0111 — the setter emission was the one pass with no dedupe at all.
    [Fact]
    public void A_partial_component_declared_twice_emits_its_setters_once()
    {
        var setters = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Widget : Component
                                                  {
                                                      public string? Note { get; set; }
                                                  }

                                                  public partial class Widget : IDisposable
                                                  {
                                                      public void Dispose() { }
                                                  }
                                                  """).Source(Setters);

        Assert.Equal(1, Count(setters, " Note(this global::Demo.Widget"));
        Assert.Equal(1, Count(setters, " __RaskResetEager_Demo_Widget("));
    }

    // The injection is per-HOST: N components produce N×(N+M) members. So the members must not carry the
    // entry's body. Each assembly emits ONE canonical entry per component into `RaskEntries{Assembly}`,
    // and the injected member is a forwarder onto it — same entry, same reset routines, same pending
    // mask, one line instead of a reset triple repeated once per host component.
    [Fact]
    public void An_injected_entry_forwards_to_the_one_canonical_entry_instead_of_repeating_it()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Page : Component { }
                                              public partial class Card : Component
                                              {
                                                  public string? Note { get; set; }
                                              }
                                              """);

        // The canonical entry: public (a referencing assembly has to reach it), and the only place the
        // reset triple is written.
        var host = run.Source("RaskBuilderEntryHost.g.cs");
        Assert.Contains("public static class RaskEntriesTestAssembly", host, StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Demo.Card Card => global::Rask.Core.BuilderRuntime.Entry<global::Demo.Card>("
            + "global::RaskBuilderSettersTestAssembly.__RaskResetEager_Demo_Card, "
            + "global::RaskBuilderSettersTestAssembly.__RaskResetPending_Demo_Card, ",
            host,
            StringComparison.Ordinal);

        // The injected member: the same entry, reached by name.
        var entries = run.Source(Entries);
        Assert.Contains(
            "    private static global::Demo.Card Card => global::RaskEntriesTestAssembly.Card;",
            entries,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__RaskResetEager_Demo_Card", entries, StringComparison.Ordinal);
    }

    // Components in a REFERENCED assembly are in neither emission: they are not Rask.Core's (whose
    // entries ride on Component itself and are inherited), and they are not in this compilation's
    // syntax. Without this they reach the builder surface not at all and stay factory-only — which is
    // what makes deleting the factory impossible. Rask.Native is a real referenced Rask assembly here.
    [Fact]
    public void A_referenced_assemblys_components_are_injected_too()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Page : Component { }
                                                  """).Source(Entries);

        Assert.Contains(
            "    private static global::Rask.Native.Components.NativeTabBar NativeTabBar "
            + "=> global::RaskEntriesRask_Native.NativeTabBar;",
            entries,
            StringComparison.Ordinal);
    }

    // Rask.Core's tags are members of Component, which every component everywhere already inherits.
    // Forwarding to them from a consumer's partial as well would HIDE the inherited member — CS0108, an
    // error under warnings-as-errors — so the assembly that declares Component is skipped, and so is any
    // referenced entry whose name Component already carries.
    [Fact]
    public void Rask_cores_own_entries_are_not_re_injected_over_the_inherited_ones()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Page : Component { }
                                                  """).Source(Entries);

        Assert.DoesNotContain("RaskEntriesRask_Core", entries, StringComparison.Ordinal);
        Assert.DoesNotContain(" Div =>", entries, StringComparison.Ordinal);
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
