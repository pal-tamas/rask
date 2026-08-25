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
                                              using System;
                                              using Rask.Core;
                                              namespace Demo
                                              {
                                                  public partial class Page : Component { }
                                              }
                                              namespace Demo.Products
                                              {
                                                  public partial class Card<T> : Component
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

        // Products.Card<T> has nothing to infer T from, so it is withheld an entry and only Orders.Card
        // is eligible — no collision. (It used to be disqualified by a required raw DELEGATE, which no
        // chain could set; that is not a disqualifier any more — see BuilderSetterEmissionTests.)
        Assert.Empty(run.WithId("RASK040"));

        // The canonical entry — the one place the reset triple is written — and the member injected into
        // every other component, which forwards to it. Both have to name the same `Card`.
        var entry = run.Source("RaskBuilderEntryHost.g.cs").Split('\n')
            .Single(l => l.Contains(" Card =>", StringComparison.Ordinal));
        Assert.Contains("Entry<global::Demo.Orders.Card>", entry, StringComparison.Ordinal);
        Assert.Contains("__RaskResetEager_Demo_Orders_Card", entry, StringComparison.Ordinal);
        Assert.Contains(
            "    private static global::Rask.Core.Build<global::Demo.Orders.Card> Card => global::RaskEntriesTestAssembly.Card;",
            run.Source(Entries),
            StringComparison.Ordinal);

        // …and that reset casts to the type the entry builds, not to the other Card. (The cast rides on
        // the PENDING half here: both Cards' own props fold, so the eager half has nothing to write.)
        Assert.Contains("__RaskResetPending_Demo_Orders_Card", entry, StringComparison.Ordinal);
        var reset = BuilderGeneratorHarness.Method(run.Source(Setters), "__RaskResetPending_Demo_Orders_Card");
        Assert.Contains("(global::Demo.Orders.Card)__c0", reset, StringComparison.Ordinal);
        Assert.Contains("__c.Other", reset, StringComparison.Ordinal);

        // …and the ineligible Card gets no reset at all, because a reset exists to serve an entry and it
        // has none. Its setters are still emitted — a chain reaches it only through a factory-built
        // receiver, and those still have to work.
    }

    // Same-named GENERIC components are the one shape that survives a shared name: their entries are
    // methods, so different arities are overloads (BsSelect<TItem> next to BsSelect<TValue, TItem>).
    [Fact]
    public void Same_named_generic_controls_share_a_seed__and_the_arity_that_cannot_pin_keeps_no_entry()
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
                                                  public Action<TItem>? OnChange { get; set; }
                                                  public Func<TItem, Task>? OnChangeAsync { get; set; }
                                                  public Expression<Func<TItem>>? Bind { get; set; }
                                                  public Validate<TItem>? Validate { get; set; }
                                                  public ValidateAsync<TItem>? ValidateAsync { get; set; }
                                                  public Action<TItem>? AfterBind { get; set; }
                                                  public Func<TItem, Task>? AfterBindAsync { get; set; }
                                              }

                                              public partial class Pick<TValue, TItem> : Component, IFormControl<TValue>
                                              {
                                                  public TValue? Value { get; set; }
                                                  public Action<TValue>? OnChange { get; set; }
                                                  public Func<TValue, Task>? OnChangeAsync { get; set; }
                                                  public Expression<Func<TValue>>? Bind { get; set; }
                                                  public Validate<TValue>? Validate { get; set; }
                                                  public ValidateAsync<TValue>? ValidateAsync { get; set; }
                                                  public Action<TValue>? AfterBind { get; set; }
                                                  public Func<TValue, Task>? AfterBindAsync { get; set; }
                                              }
                                              """);

        Assert.Empty(run.WithId("RASK040"));
        var entries = run.Source(Entries);

        // Two arities of one name share the one entry member, so they share its seed. Each contributes
        // the openings it can COMPLETE: `Pick<TItem>` pins TItem from its bind expression and opens;
        // `Pick<TValue, TItem>` has nothing that pins TItem at all, so it contributes none and keeps no
        // entry. That is the same finding that retired BsSelect's second arity — and it is also what
        // stops the two openings colliding, since both would otherwise be `Bind<T>(Expression<Func<T>>)`
        // on the same receiver (CS0111).
        Assert.Contains("RaskSeed_Pick", entries, StringComparison.Ordinal);

        // The steps live with the seed in the host file — a consuming assembly's components publish one
        // canonical entry there and forward to it, rather than re-emitting it per host.
        var host = run.Source("RaskBuilderEntryHost.g.cs");
        Assert.Contains(
            "global::Rask.Core.Build<global::Demo.Pick<TItem>, global::Rask.Core.Forms.Bound> Bind<TItem>(",
            host, StringComparison.Ordinal);
        Assert.DoesNotContain("Pick<TValue, TItem> Bind", host, StringComparison.Ordinal);
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

        // `T?`, not `T`: a step takes the property's DECLARED type, so a nullable one stays nullable and
        // a chain that passes a `string?` still infers `string` rather than `string?`.
        Assert.Contains("global::Rask.Core.Build<global::Demo.Holder<T>> Item<T>(T? Item)", host, StringComparison.Ordinal);

        // …and it leaves the property exactly as the property's own setter would. Folding the change
        // keeps propsChanged honest; clearing the pending bit stops the deferred reset putting back the
        // value the entry just set, which would blank it on the very first render.
        Assert.Contains("BuilderRuntime.Track(__c, __c.Item, Item)", host, StringComparison.Ordinal);
        Assert.Contains("BuilderRuntime.Written(__c,", host, StringComparison.Ordinal);
        Assert.Contains("__c.Item = Item;", host, StringComparison.Ordinal);
    }

    // A property does not have to BE the type parameter to pin it — a sequence of it will do, and
    // inference reads straight through. That widening is what gives `BsDataGrid` its `.Data(rows)` and
    // `.Columns(cols)` openings, and `Grid<T>` here its `.Rows(…)`: without it a generic component whose
    // properties merely mention T had no way in at all and kept the factory.
    //
    // The narrow rule was deliberate once, to avoid handing an entry to components nobody had scheduled
    // to migrate. With the factory going away that reasoning inverts: an unreachable component is the
    // failure, not the surprise.
    [Fact]
    public void A_generic_component_is_pinned_by_a_property_that_merely_mentions_its_type_argument()
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
        Assert.Contains("RaskSeed_Grid", host, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Build<global::Demo.Grid<T>> Rows<T>(", host, StringComparison.Ordinal);

        // …and a non-generic component with nothing required still hands back the component itself, so
        // the seed is only ever paid for where something has to be settled first.
        Assert.Contains(" Plain =>", host, StringComparison.Ordinal);
    }

    // A required DELEGATE used to be the one shape that blocked an entry, and it was never about
    // construction: the prop was invocable, so `x.Template(fn)` bound to the property and a same-named
    // setter could never be reached — an entry would have been constructible and never completable. The
    // `Build<TComponent>` receiver settles it, so the component gets an entry and `Template` opens it.
    [Fact]
    public void A_component_with_a_required_delegate_gets_an_entry_that_demands_it()
    {
        var run = BuilderGeneratorHarness.Run("""
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
                                              """);

        var entries = run.Source(Entries);
        Assert.Contains(" Templated =>", entries, StringComparison.Ordinal);
        Assert.Contains(" Optional =>", entries, StringComparison.Ordinal);

        // A required property is a chain STEP, so the entry hands back a seed and `Template` is the way
        // out of it — the component does not exist until it has been supplied.
        var host = run.Source("RaskBuilderEntryHost.g.cs");
        Assert.Contains("RaskSeed_Templated", host, StringComparison.Ordinal);
        Assert.Contains("Template(", host, StringComparison.Ordinal);
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

        Assert.Equal(1, Count(setters, " Note(this global::Rask.Core.Build<global::Demo.Widget>"));
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
            "public static global::Rask.Core.Build<global::Demo.Card> Card => new(global::Rask.Core.BuilderRuntime.Entry<global::Demo.Card>("
            + "global::RaskBuilderSettersTestAssembly.__RaskResetEager_Demo_Card, "
            + "global::RaskBuilderSettersTestAssembly.__RaskResetPending_Demo_Card, ",
            host,
            StringComparison.Ordinal);

        // The injected member: the same entry, reached by name.
        var entries = run.Source(Entries);
        Assert.Contains(
            "    private static global::Rask.Core.Build<global::Demo.Card> Card => global::RaskEntriesTestAssembly.Card;",
            entries,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__RaskResetEager_Demo_Card", entries, StringComparison.Ordinal);
    }

    // Components in a REFERENCED assembly are in neither emission: they are not Rask.Core's (whose
    // entries ride on Component itself and are inherited), and they are not in this compilation's
    // syntax. Without this they reach the builder surface not at all and stay factory-only — which is
    // what makes deleting the factory impossible. Rask.Html is a real referenced Rask assembly here.
    [Fact]
    public void A_referenced_assemblys_components_are_injected_too()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Page : Component { }
                                                  """).Source(Entries);

        Assert.Contains("global::RaskEntriesRask_Html.", entries, StringComparison.Ordinal);
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

    // An ABSTRACT component gets no entry of its own — nothing can construct it — but it is still a
    // component, and the builder surface is only reachable from INSIDE one. Skipping it as an injection
    // host left every abstract base (BsBlock, BsFormControl<T>, PollingPanel) able to name no entry at
    // all, with no diagnostic: the calls simply bound to the factory instead, and CS0119 is what the
    // author saw once the factory went away.
    [Fact]
    public void An_abstract_component_base_is_injected_into_even_though_it_has_no_entry()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public abstract partial class PanelBase : Component
                                                  {
                                                      public string? Class { get; set; }
                                                  }
                                                  public partial class Card : Component
                                                  {
                                                      public string? Note { get; set; }
                                                  }
                                                  """).Source(Entries);

        Assert.Contains("partial class PanelBase\n", entries.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "    private static global::Rask.Core.Build<global::Demo.Card> Card => global::RaskEntriesTestAssembly.Card;",
            entries,
            StringComparison.Ordinal);

        // …and it is only a host. An abstract type cannot be built, so it never publishes an entry.
        Assert.DoesNotContain(" PanelBase =>", entries, StringComparison.Ordinal);
    }

    // A generic abstract base is injected the same way. A partial declaration may omit the constraint
    // clauses the other declaration states, so the injected half restates only the type parameters.
    [Fact]
    public void A_generic_abstract_base_is_injected_without_restating_its_constraints()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public abstract partial class ControlBase<T> : Component
                                                      where T : notnull
                                                  {
                                                  }
                                                  public partial class Card : Component { }
                                                  """).Source(Entries);

        Assert.Contains("partial class ControlBase<T>", entries, StringComparison.Ordinal);
        Assert.DoesNotContain("where T : notnull", entries, StringComparison.Ordinal);
    }

    // The forwarders are `private static`, so a base and its subclass both carrying them is not hiding —
    // CS0108 only fires for an inherited member the derived type can SEE. That is also why the base
    // cannot stand in for its subclasses: each class needs its own copy.
    [Fact]
    public void A_subclass_of_an_injected_base_still_gets_its_own_copy()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public abstract partial class PanelBase : Component { }
                                                  public partial class Panel : PanelBase { }
                                                  public partial class Card : Component { }
                                                  """).Source(Entries);

        Assert.Equal(2, Count(entries, "private static global::Rask.Core.Build<global::Demo.Card> Card =>"));
    }

    // RASK036 is the diagnostic for a component that cannot receive entries because there is no partial
    // to inject into. An abstract base is a host now, so it is held to the same rule.
    [Fact]
    public void A_non_partial_abstract_base_is_reported_as_RASK036()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public abstract class PanelBase : Component { }
                                              public partial class Card : Component { }
                                              """);

        Assert.Contains(run.WithId("RASK036"), d => d.GetMessage().Contains("PanelBase", StringComparison.Ordinal));
    }

    // A type that is NOT a component reaches the surface by deriving from RaskMarkup — Component's own
    // base, where the framework tags are emitted. That inheritance is the whole of the framework half;
    // the consumer's own components still have to be injected, exactly as they are into a component,
    // which is what this pins.
    [Fact]
    public void A_RaskMarkup_host_receives_the_consumers_own_entries()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Card : Component { }
                                                  public partial class CardTests : RaskMarkup { }
                                                  """).Source(Entries);

        // Card itself cannot carry an entry named Card (CS0542) and does not need one, so the markup
        // host is the only place the forwarder lands.
        Assert.Contains("partial class CardTests", entries, StringComparison.Ordinal);
        Assert.Equal(1, Count(entries, "private static global::Rask.Core.Build<global::Demo.Card> Card =>"));
    }

    // A markup host is one that names RaskMarkup DIRECTLY. A subclass of one already has the framework
    // entries by ordinary inheritance, and making it a host as well would mean demanding `partial` of
    // every subclass of a shared test base — an error, under warnings-as-errors, in files that name no
    // markup at all, produced by a one-line edit to something else. Injection follows the declaration
    // that opted in.
    [Fact]
    public void A_subclass_of_a_markup_host_is_not_itself_a_host()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Card : Component { }
                                              public partial class TestBase : RaskMarkup { }
                                              public class ConcreteTests : TestBase { }
                                              """);

        Assert.Empty(run.WithId("RASK036"));
        Assert.DoesNotContain("partial class ConcreteTests", run.Source(Entries), StringComparison.Ordinal);
    }

    // Same rule as a component: no partial, nowhere to inject.
    [Fact]
    public void A_non_partial_markup_host_is_reported_as_RASK036()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Card : Component { }
                                              public class CardTests : RaskMarkup { }
                                              """);

        Assert.Contains(run.WithId("RASK036"), d => d.GetMessage().Contains("CardTests", StringComparison.Ordinal));
    }

    // [RaskMarkup] on a type whose base slot is still free costs nothing extra: the generated partial
    // writes `: RaskMarkup` for it, so the framework tags arrive by the same inheritance the base-class
    // form uses, and the attribute is only a way of saying it without spending the slot YOURSELF. What
    // must NOT happen is the expensive delivery — forwarding to a name you already inherit is CS0108.
    [Fact]
    public void An_attributed_host_with_a_free_base_slot_is_given_the_base_rather_than_the_entries()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Card : Component { }
                                                  [RaskMarkup]
                                                  public partial class CardTests { }
                                                  """).Source(Entries);

        Assert.Contains("partial class CardTests : global::Rask.Core.RaskMarkup", entries,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::RaskEntriesRask_Core.", entries, StringComparison.Ordinal);
        Assert.Equal(1, Count(entries, "private static global::Rask.Core.Build<global::Demo.Card> Card =>"));
    }

    // The shape the attribute exists for: the base slot belongs to someone else, so no amount of
    // generated source can make this type inherit anything. The framework tags are injected as members
    // instead, forwarding to the `RaskEntriesRaskCore` class Rask.Core publishes for exactly this.
    [Fact]
    public void An_attributed_host_whose_base_is_taken_receives_the_framework_entries_as_members()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public abstract class TestBed { }
                                                  public partial class Card : Component { }
                                                  [RaskMarkup]
                                                  public partial class CardTests : TestBed { }
                                                  """).Source(Entries);

        Assert.Contains("partial class CardTests\n", entries.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "private static global::Rask.Core.Build<global::Rask.Core.Components.Div> Div => global::RaskEntriesRask_Core.Div;", entries,
            StringComparison.Ordinal);
        // The consumer's own components come the usual way, alongside them.
        Assert.Equal(1, Count(entries, "private static global::Rask.Core.Build<global::Demo.Card> Card =>"));
    }

    // A `static class` can derive from nothing at all, which is why DemoRegistry had to stop being one.
    // With the attribute it does not: the generated partial repeats the `static` modifier and carries
    // the same injected surface.
    [Fact]
    public void An_attributed_static_class_is_a_host_and_stays_static()
    {
        var entries = BuilderGeneratorHarness.Run("""
                                                  using Rask.Core;
                                                  namespace Demo;
                                                  public partial class Card : Component { }
                                                  [RaskMarkup]
                                                  public static partial class Demos { }
                                                  """).Source(Entries);

        Assert.Contains("static partial class Demos", entries, StringComparison.Ordinal);
        Assert.DoesNotContain("static partial class Demos : ", entries, StringComparison.Ordinal);
        Assert.Contains(
            "private static global::Rask.Core.Build<global::Rask.Core.Components.Div> Div => global::RaskEntriesRask_Core.Div;", entries,
            StringComparison.Ordinal);
    }

    // The contagion rule, restated for the attribute. It holds by construction rather than by policy:
    // GetAttributes() reports what was written on THIS declaration, so a subclass of an attributed host
    // is never one — no 'partial' is demanded of it, and no RASK036 lands in a file that names no markup.
    [Fact]
    public void A_subclass_of_an_attributed_host_is_not_itself_a_host()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public abstract class TestBed { }
                                              public partial class Card : Component { }
                                              [RaskMarkup]
                                              public partial class TestBase : TestBed { }
                                              public class ConcreteTests : TestBase { }
                                              """);

        Assert.Empty(run.WithId("RASK036"));
        Assert.DoesNotContain("partial class ConcreteTests", run.Source(Entries), StringComparison.Ordinal);
    }

    // Same rule again, and a harder consequence: an attributed host with no partial loses the framework
    // tags too, because the generated partial is where its base would have come from. RASK036 has to say
    // so rather than repeating the sentence written for a component.
    [Fact]
    public void A_non_partial_attributed_host_is_told_it_loses_the_framework_tags_as_well()
    {
        var run = BuilderGeneratorHarness.Run("""
                                              using Rask.Core;
                                              namespace Demo;
                                              public partial class Card : Component { }
                                              [RaskMarkup]
                                              public class CardTests { }
                                              """);

        var message = Assert.Single(run.WithId("RASK036")).GetMessage();
        Assert.Contains("CardTests", message, StringComparison.Ordinal);
        Assert.Contains("framework tags", message, StringComparison.Ordinal);
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
