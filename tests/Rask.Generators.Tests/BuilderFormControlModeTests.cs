using System.Linq;
namespace Rask.Generators.Tests;

// A form control's chain carries the MODE its entry step opened in — Build<TControl, Bound> or
// Build<TControl, Controlled> — and each mode's steps are declared only on their own mode. So the
// chain cannot say something the control will not read: a bound control derives its value and its
// checked state from the model and installs its own write-back, so Value/Checked/OnInput/OnChange are
// dead there, and a controlled one parses no expression, so Validate/AfterBind are dead in it.
//
// Before this the steps were all on one Build<TControl> and the ones that did not apply were accepted
// and silently dropped at render time.
public class BuilderFormControlModeTests
{
    // A generic control: Bind and Value pin T, so they already opened the chain. Checked and OnInput
    // are the control's OWN props (as on Input/Textarea), not interface members — they are recognized
    // by name, the same way every other form-control member is.
    private const string Widget = """
                                  using System;
                                  using System.Linq.Expressions;
                                  using System.Threading.Tasks;
                                  using Rask.Core;
                                  using Rask.Core.Forms;
                                  namespace Demo;
                                  public partial class Widget<T> : Component, IFormControl<T>
                                  {
                                      public T? Value { get; set; }
                                      public Callback<T>? OnChange { get; set; }
                                      public Expression<Func<T>>? Bind { get; set; }
                                      public Validate<T>? Validate { get; set; }
                                      public ValidateAsync<T>? ValidateAsync { get; set; }
                                      public Callback<T>? AfterBind { get; set; }
                                      public bool? Checked { get; set; }
                                      public Callback<string>? OnInput { get; set; }
                                      public string? Label { get; set; }
                                  }
                                  """;

    // A NON-generic control (BsCheck's shape): it pins nothing and demands nothing, so it used to have
    // no seed at all — Bind and Value were both plain setters and one chain could take both.
    private const string Flag = """
                                using System;
                                using System.Linq.Expressions;
                                using System.Threading.Tasks;
                                using Rask.Core;
                                using Rask.Core.Forms;
                                namespace Demo;
                                public partial class Flag : Component, IFormControl<bool>
                                {
                                    public bool Value { get; set; } = false;
                                    public Action<bool>? OnChange { get; set; }
                                    public Expression<Func<bool>>? Bind { get; set; }
                                    public Validate<bool>? Validate { get; set; }
                                    public ValidateAsync<bool>? ValidateAsync { get; set; }
                                    public Action<bool>? AfterBind { get; set; }
                                    public string? Label { get; set; }
                                }
                                """;

    // A control over a VALUE TYPE with a required step of its own — the kit's UiCheckbox/UiRating
    // shape. `bool Value` has no initializer, so RASK001's rule reads it as required.
    private const string Ticked = """
                                  using System;
                                  using System.Linq.Expressions;
                                  using System.Threading.Tasks;
                                  using Rask.Core;
                                  using Rask.Core.Forms;
                                  namespace Demo;
                                  public partial class Ticked : Component, IFormControl<bool>
                                  {
                                      public required string Label { get; set; }
                                      public bool Value { get; set; }
                                      public Action<bool>? OnChange { get; set; }
                                      public Expression<Func<bool>>? Bind { get; set; }
                                      public Validate<bool>? Validate { get; set; }
                                      public ValidateAsync<bool>? ValidateAsync { get; set; }
                                      public Action<bool>? AfterBind { get; set; }
                                  }
                                  """;

    // …and the generic version of the same shape, whose required step pins nothing.
    private const string Named = """
                                 using System;
                                 using System.Linq.Expressions;
                                 using System.Threading.Tasks;
                                 using Rask.Core;
                                 using Rask.Core.Forms;
                                 namespace Demo;
                                 public partial class Named<T> : Component, IFormControl<T>
                                 {
                                     public required string Label { get; set; }
                                     public T? Value { get; set; }
                                     public Callback<T>? OnChange { get; set; }
                                     public Expression<Func<T>>? Bind { get; set; }
                                     public Validate<T>? Validate { get; set; }
                                     public ValidateAsync<T>? ValidateAsync { get; set; }
                                     public Callback<T>? AfterBind { get; set; }
                                 }
                                 """;

    [Theory]
    [InlineData("Checked")]
    [InlineData("OnChange")]
    [InlineData("OnInput")]
    public void A_controlled_step_is_declared_only_on_the_controlled_mode(string step)
    {
        var sig = Signature(Setters(Widget), step);

        Assert.Contains("global::Rask.Core.Forms.Controlled>", sig, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Rask.Core.Forms.Bound>", sig, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Validate")]
    [InlineData("ValidateAsync")]
    [InlineData("AfterBind")]
    public void A_bound_step_is_declared_only_on_the_bound_mode(string step)
    {
        var sig = Signature(Setters(Widget), step);

        Assert.Contains("global::Rask.Core.Forms.Bound>", sig, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Rask.Core.Forms.Controlled>", sig, StringComparison.Ordinal);
    }

    // The other half, and the one that makes the split worth having: a display prop belongs to neither
    // mode, so it is written over an OPEN TMode and stays reachable from both — and hands the mode
    // back, so the step after it still knows which one the chain is in. A control that could not say
    // `.Label(…)` after `.Bind(…)` would be no trade at all.
    [Fact]
    public void A_shared_step_keeps_the_mode_open()
    {
        var sig = Signature(Setters(Widget), "Label");

        Assert.Contains("global::Rask.Core.Build<global::Demo.Widget<T>, TMode> Label<T, TMode>(", sig,
            StringComparison.Ordinal);
    }

    // The shared Element/Component surface is emitted over BOTH chain shapes for the same reason: a form
    // control is an element too, and one that could not say `.Class(…)` would be no trade at all. It is
    // emitted by the assembly that DECLARES Element, which is why the source below declares it.
    [Fact]
    public void The_shared_surface_is_emitted_for_both_chain_shapes()
    {
        var output = Setters("""
                             namespace Rask.Core;
                             public abstract partial class Element : Component
                             {
                                 public string? Class { get; set; }
                             }
                             """);

        Assert.Contains(
            "global::Rask.Core.Build<T> Class<T>(this global::Rask.Core.Build<T> __b, string? value) "
            + "where T : global::Rask.Core.Element",
            output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Core.Build<T, TMode> Class<T, TMode>(this global::Rask.Core.Build<T, TMode> __b, "
            + "string? value) where T : global::Rask.Core.Element",
            output, StringComparison.Ordinal);
    }

    // The entry is where the mode is chosen, so it is where the mode is fixed. `Of` states the type
    // argument and supplies no value, which leaves the parent owning one: controlled.
    [Fact]
    public void The_entry_steps_fix_the_mode()
    {
        var output = Entries(Widget);

        Assert.Contains("global::Rask.Core.Build<global::Demo.Widget<T>, global::Rask.Core.Forms.Bound> Bind<T>(",
            output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Core.Build<global::Demo.Widget<T>, global::Rask.Core.Forms.Controlled> Value<T>(",
            output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Core.Build<global::Demo.Widget<T>, global::Rask.Core.Forms.Controlled> Of<T>(",
            output, StringComparison.Ordinal);
    }

    // A non-generic control pins nothing, so nothing forced a seed on it and both mode setters were
    // reachable from one chain. It gets a seed now BECAUSE it is a form control.
    [Fact]
    public void A_non_generic_control_still_opens_on_its_mode()
    {
        var output = Entries(Flag);

        Assert.Contains("readonly struct RaskSeed_Flag", output, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Build<global::Demo.Flag, global::Rask.Core.Forms.Bound> Bind(",
            output, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Build<global::Demo.Flag, global::Rask.Core.Forms.Controlled> Value(",
            output, StringComparison.Ordinal);
    }

    // Bind never folds into propsChanged. An expression tree is a fresh object every render and the eager
    // reset blanks it first, so a Track call compares a new tree against null and reports a change on
    // EVERY frame — which costs a bound control the render cache outright. PinCandidates (the generic
    // path) and EmitBoundSetters both already say Track: false; the non-generic opening has to agree.
    [Fact]
    public void The_Bind_opening_does_not_fold_into_props_changed()
    {
        var bind = Entries(Flag).Split('\n')
            .SkipWhile(l => !l.Contains("Forms.Bound> Bind(", StringComparison.Ordinal))
            .Take(6)
            .ToList();

        Assert.DoesNotContain(bind, l => l.Contains("BuilderRuntime.Track", StringComparison.Ordinal));
        Assert.DoesNotContain(bind, l => l.Contains("BuilderRuntime.Written", StringComparison.Ordinal));

        // …while Value is an ordinary value prop and folds like one, or a controlled control would stop
        // reporting real changes.
        var value = Entries(Flag).Split('\n')
            .SkipWhile(l => !l.Contains("Forms.Controlled> Value(", StringComparison.Ordinal))
            .Take(6)
            .ToList();
        Assert.Contains(value, l => l.Contains("BuilderRuntime.Track", StringComparison.Ordinal));
    }

    // …and the mode steps are then not ALSO setters, or choosing one would not rule out the other.
    [Theory]
    [InlineData("Bind")]
    [InlineData("Value")]
    public void A_mode_step_is_not_also_a_setter(string step)
    {
        Assert.DoesNotContain(" " + step + "(this global::Rask.Core.Build<global::Demo.Flag", Setters(Flag),
            StringComparison.Ordinal);
    }

    // An ordinary component is untouched: no mode, no extra type parameter, the same `Build<T>` chain.
    [Fact]
    public void A_component_that_is_not_a_form_control_has_no_mode()
    {
        var output = Setters("""
                             using Rask.Core;
                             namespace Demo;
                             public partial class Card : Component
                             {
                                 public string? Title { get; set; }
                             }
                             """);

        Assert.Contains("global::Rask.Core.Build<global::Demo.Card> Title(this global::Rask.Core.Build<global::Demo.Card> __b",
            output, StringComparison.Ordinal);
    }

    // A form control's Value is its OPENING, never a step still outstanding after one.
    //
    // It only looks required on a control closed over a VALUE TYPE: `IFormControl<T>` declares `T?
    // Value`, where `?` over an unconstrained T is a nullability annotation, so `IFormControl<bool>`
    // has a plain `bool Value` and RASK001's rule reads it as required. Left in the required set it is
    // unsatisfiable in bound mode, which withdraws Value on purpose — the chain sits in a pending state
    // waiting for a step its own mode does not offer, and the only symptom is that it has no ToHtml.
    [Fact]
    public void A_value_types_Value_is_the_opening_rather_than_an_outstanding_step()
    {
        var output = Entries(Ticked);

        // One required step is left (Label), so there is no state named for Value at all.
        Assert.DoesNotContain("RaskPending_Ticked_Value", output, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskPending_Ticked_Label", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_chain_over_a_value_type_can_still_be_completed()
    {
        // The assertion the previous one exists for: taking the ONE outstanding step hands back the
        // finished chain rather than another pending state.
        Assert.Contains(
            "global::Rask.Core.Build<global::Demo.Ticked, TMode> Label(string Label)",
            Entries(Ticked), StringComparison.Ordinal);
    }

    // `Of` is offered to a generic form control even though it has a required step, and that is the
    // same rule read properly rather than an exception to it: a form control's openings are its MODE
    // pins, so a required step of its own is never one and never gets to pin the type. `Label` says
    // nothing about T, so without this a controlled call site with no starting value has no way in.
    [Fact]
    public void A_generic_control_with_a_required_step_still_opens_on_its_type_alone()
    {
        Assert.Contains(
            "global::Rask.Core.Build<global::Demo.Named<T>, global::Rask.Core.Forms.Controlled> Of<T>(",
            Entries(Named), StringComparison.Ordinal);
    }

    // The emitter writes each signature on one line, so a signature IS a line.
    // Every overload of the step, joined. A carrier-typed member emits THREE — the carrier pass-through
    // and a bare delegate shape for each of sync and async — and the mode rule has to hold for all of
    // them. Taking `Single()` here would throw on a carrier step and, worse, would have silently passed
    // had only one of the three been on the wrong mode.
    private static string Signature(string output, string step)
    {
        var lines = output.Split('\n')
            .Where(l => l.Contains(" " + step + "<", StringComparison.Ordinal)
                        && l.Contains("(this ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(lines);
        return string.Join("\n", lines);
    }

    private static string Setters(string source) =>
        BuilderGeneratorHarness.Run(source).Source("RaskBuilderSetters.g.cs");

    private static string Entries(string source) =>
        BuilderGeneratorHarness.Run(source).Source("RaskBuilderEntryHost.g.cs");
}
