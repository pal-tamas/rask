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
                                                      public required string Label { get; set; }
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

        // Products.Card has a `required` member, so only Orders.Card is eligible — no collision.
        Assert.Empty(run.WithId("RASK040"));

        var entry = run.Source(Entries).Split('\n')
            .Single(l => l.Contains(" Card =>", StringComparison.Ordinal));
        Assert.Contains("Entry<global::Demo.Orders.Card>", entry, StringComparison.Ordinal);
        Assert.Contains("__RaskResetEager_Demo_Orders_Card", entry, StringComparison.Ordinal);

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
    // factory demands it on every call and re-assigns it on every render. An entry has no argument to
    // carry it and no default to reset it to, so `Widget.Title("x")` on one render and a bare `Widget`
    // on the next would silently keep the title — and the very first render would leave it `null!`.
    // Those components stay on their factory, exactly as a `required` member's do.
    [Fact]
    public void A_component_with_a_required_factory_param_gets_no_entry()
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
        Assert.DoesNotContain(" Widget =>", entries, StringComparison.Ordinal);

        // …and a component whose props are all optional still gets one, so this is the required prop
        // doing the work rather than the emission having stopped.
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
