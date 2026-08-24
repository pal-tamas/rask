namespace Rask.Generators.Tests;

/// <summary>
///     Which of a component's properties the generator turns into a chain step, and which it leaves alone.
/// </summary>
/// <remarks>
///     <para>
///         These rules used to be pinned against the generated factory's parameter list, which is where
///         they were easiest to read: a property that became a step was a parameter you could see. With the
///         factory gone they are read off the setter surface instead — a property that becomes a step gets
///         an extension method named after it on <c>Build&lt;T&gt;</c>, and one that does not, does not.
///     </para>
///     <para>
///         The rules themselves did not change: <c>GetFactoryProperties</c> was always shared by both
///         surfaces, so a property excluded from one was excluded from the other. What is gone is only the
///         half of those tests that was about parameter ORDER, which a chain has no equivalent of — every
///         step names the property it sets, so nothing can silently rebind.
///     </para>
///     <para>
///         The behaviour of a step once it exists — folding, resets, auto-wrapping, required steps, generic
///         inference, construction through <c>ActivatorUtilities</c> — lives in
///         <c>BuilderSetterEmissionTests</c> and <c>BuilderEntryEmissionTests</c>.
///     </para>
/// </remarks>
public class ChainPropertySelectionTests
{
    private static string Setters(string body)
    {
        var run = GeneratorDriverFixture.Run($$"""
                                               using System;
                                               using System.Collections.Generic;
                                               using Rask.Core;
                                               namespace Demo;
                                               public sealed partial class Widget : Component
                                               {
                                               {{body}}
                                                   public override Component? Render() => this;
                                               }
                                               """);
        return run.GeneratedSource("RaskBuilderSetters.g.cs");
    }

    private static bool HasSetter(string output, string name) =>
        output.Contains($"> {name}(this global::Rask.Core.Build<global::Demo.Widget>", StringComparison.Ordinal);

    [Fact]
    public void A_settable_property_becomes_a_step() =>
        Assert.True(HasSetter(Setters("    public string? Name { get; set; }"), "Name"));

    [Fact]
    public void SkipChain_removes_the_step() =>
        Assert.False(HasSetter(
            Setters("    [SkipFactory]\n    public string? Name { get; set; }"), "Name"));

    [Fact]
    public void A_private_setter_is_not_a_step() =>
        Assert.False(HasSetter(Setters("    public string? Name { get; private set; }"), "Name"));

    [Fact]
    public void A_static_property_is_not_a_step() =>
        Assert.False(HasSetter(Setters("    public static string? Name { get; set; }"), "Name"));

    [Fact]
    public void An_init_only_setter_is_not_a_step() =>
        // The one rule the chain has that the factory did not. A step is an extension method that assigns
        // `__c.Name = value` AFTER the component exists, and an `init` accessor is callable only from an
        // object initializer — so there is no step it could compile into. The factory could set one,
        // because it constructed with `new T { Name = … }`; the chain constructs first and assigns after.
        Assert.False(HasSetter(Setters("    public string? Name { get; init; }"), "Name"));

    [Fact]
    public void A_constant_member_initializer_leaves_the_step_in_place() =>
        // The initializer becomes the step's DEFAULT — the value the reset puts back when a chain stops
        // naming it — not a reason to withhold the step. BuilderSetterEmissionTests pins the reset itself.
        Assert.True(HasSetter(Setters("    public string Tag { get; set; } = \"x\";"), "Tag"));

    [Fact]
    public void A_non_constant_member_initializer_removes_the_step() =>
        // Nothing could restore it: the reset writes a literal, and this value is only knowable by running
        // the constructor. Excluded outright rather than reset to something it never was.
        Assert.False(HasSetter(
            Setters("    public List<string> Tags { get; set; } = new List<string>();"), "Tags"));

    [Fact]
    public void Children_is_the_indexer_not_a_step() =>
        // Children arrive through `Component this[params Component[]]`. A step of the same name would be a
        // second way to say it, and the two would disagree.
        Assert.False(HasSetter(
            Setters("    public IEnumerable<Component>? Children { get; set; }"), "Children"));

    [Fact]
    public void The_name_Children_is_reserved_whatever_its_type() =>
        // The factory excluded `Children` on name AND type, so a `string Children` was an ordinary
        // parameter there. The chain excludes it on the NAME alone: the indexer owns that word, and a step
        // sharing it would read as the children syntax while setting something else.
        Assert.False(HasSetter(Setters("    public string? Children { get; set; }"), "Children"));

    [Fact]
    public void A_base_class_property_gets_a_step_on_the_derived_component()
    {
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public abstract partial class Base : Component
                                             {
                                                 public string? Shared { get; set; }
                                             }
                                             public sealed partial class Widget : Base
                                             {
                                                 public string? Own { get; set; }
                                                 public override Component? Render() => this;
                                             }
                                             """);
        var output = run.GeneratedSource("RaskBuilderSetters.g.cs");

        Assert.True(HasSetter(output, "Shared"));
        Assert.True(HasSetter(output, "Own"));
    }

    [Fact]
    public void An_abstract_property_overridden_in_the_derived_class_gets_one_step_not_two()
    {
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public abstract partial class Base : Component
                                             {
                                                 public abstract string? Title { get; set; }
                                             }
                                             public sealed partial class Widget : Base
                                             {
                                                 public override string? Title { get; set; }
                                                 public override Component? Render() => this;
                                             }
                                             """);
        var output = run.GeneratedSource("RaskBuilderSetters.g.cs");

        Assert.Equal(
            1,
            output.Split("> Title(this global::Rask.Core.Build<global::Demo.Widget>").Length - 1);
    }

    // ---- RASK001 / RASK002 ------------------------------------------------------------------------
    // Reported straight off the property, with nothing generated from it involved.

    [Fact]
    public void A_non_nullable_property_with_no_initializer_raises_Rask001()
    {
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public sealed partial class Widget : Component
                                             {
                                                 public string Name { get; set; }
                                                 public override Component? Render() => this;
                                             }
                                             """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK001");
    }

    [Fact]
    public void The_required_keyword_answers_Rask001()
    {
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public sealed partial class Widget : Component
                                             {
                                                 public required string Name { get; set; }
                                                 public override Component? Render() => this;
                                             }
                                             """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK001");
    }

    [Fact]
    public void A_required_property_with_an_initializer_and_a_parameterless_ctor_raises_Rask002()
    {
        // The one shape the chain cannot honour: with a parameterless constructor present the entry uses
        // it, and a property carrying a member initializer is not one the steps can set — so nothing ever
        // assigns it and the consumer build fails with CS9035.
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public interface IClock { }
                                             public sealed partial class Widget : Component
                                             {
                                                 public Widget() { }
                                                 public Widget(IClock clock) { }
                                                 public required string Name { get; set; } = "x";
                                                 public override Component? Render() => this;
                                             }
                                             """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK002");
    }

    [Fact]
    public void A_required_property_with_a_DI_ctor_alone_does_not_raise_Rask002()
    {
        // No parameterless constructor means the entry builds through ActivatorUtilities, which runs the DI
        // constructor and leaves the steps to assign the rest — so `required` IS honoured.
        var run = GeneratorDriverFixture.Run("""
                                             using Rask.Core;
                                             namespace Demo;
                                             public interface IClock { }
                                             public sealed partial class Widget : Component
                                             {
                                                 public Widget(IClock clock) { }
                                                 public required string Name { get; set; }
                                                 public override Component? Render() => this;
                                             }
                                             """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK002");
    }
}
