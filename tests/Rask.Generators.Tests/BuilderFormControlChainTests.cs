using System.Linq;

namespace Rask.Generators.Tests;

// A form control's chain is the CONTROL, like every other chain. There is no mode in the type.
//
// There used to be: the chain was a `Build<TControl, Bound>` or a `Build<TControl, Controlled>`, each
// mode's steps were declared only on its own mode, and taking a step from the other was a compile error.
// That is what forced required steps to come first and produced the mode machinery whose members kept
// going silently dead. It is gone — the steps are ordinary extensions on the control, any order compiles,
// and the two things the type system used to say are said in words instead: a required step never taken
// is RASK038, and `Bind` together with `Value` is RASK076.
//
// What these tests hold onto is the part that was never about the type system: the RUNTIME difference
// between the two openings, which no diagnostic can state and which costs a bound control its render
// cache when it regresses.
public class BuilderFormControlChainTests
{
    // A generic control. Checked and OnInput are the control's OWN props (as on Input/Textarea), not
    // interface members — they are recognized by name, like every other form-control member.
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
                                      public Validator<T>? Validate { get; set; }
                                      public Callback<T>? AfterBind { get; set; }
                                      public bool? Checked { get; set; }
                                      public Callback<string>? OnInput { get; set; }
                                      public string? Label { get; set; }
                                  }
                                  """;

    // A NON-generic control (BsCheck's shape): it pins nothing and demands nothing.
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
                                    public Validator<bool>? Validate { get; set; }
                                    public Action<bool>? AfterBind { get; set; }
                                    public string? Label { get; set; }
                                }
                                """;

    // A generic control with a required step of its own, whose required step pins nothing.
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
                                     public Validator<T>? Validate { get; set; }
                                     public Callback<T>? AfterBind { get; set; }
                                 }
                                 """;

    // THE contract this file exists for, and the only one no diagnostic could take over.
    //
    // Bind never folds into propsChanged. An expression tree is a fresh object every render and the eager
    // reset blanks it first, so a Track call compares a new tree against null and reports a change on
    // EVERY frame — which costs a bound control the render cache outright. It is invisible in the markup
    // and invisible in the rendered HTML; the only symptom is that a bound control re-renders forever.
    [Fact]
    public void The_Bind_opening_does_not_fold_into_props_changed()
    {
        var bind = Opening(Entries(Flag), "Bind(");

        Assert.DoesNotContain(bind, l => l.Contains("BuilderRuntime.Track", StringComparison.Ordinal));
        Assert.DoesNotContain(bind, l => l.Contains("BuilderRuntime.Written", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Value_opening_does_fold_into_props_changed()
    {
        // The other half of it: Value is an ordinary value prop, and a controlled control that stopped
        // folding would stop reporting real changes.
        var value = Opening(Entries(Flag), "Value(");

        Assert.Contains(value, l => l.Contains("BuilderRuntime.Track", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_openings_hand_back_the_control_itself()
    {
        var output = Entries(Flag);

        // No mode, no wrapper: the chain IS the control, so the step after either opening is the same
        // step and the two openings are interchangeable as far as the type system is concerned.
        Assert.Contains(
            "public global::Demo.Flag Bind(", output, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Demo.Flag Value(", output, StringComparison.Ordinal);
    }

    // Every step is now reachable from one chain — which is the redesign, not an oversight. A control
    // that took both openings, or a controlled one given a Validate, is RASK076/RASK038 at the call site
    // rather than a member the compiler withheld.
    [Theory]
    [InlineData("Validate")]
    [InlineData("AfterBind")]
    [InlineData("Checked")]
    [InlineData("OnInput")]
    [InlineData("OnChange")]
    public void Every_step_of_a_form_control_is_reachable_from_one_chain(string step)
    {
        var setters = Setters(Widget);

        Assert.Contains(
            " " + step + "<T>(this global::Demo.Widget<T> __b", setters, StringComparison.Ordinal);
    }

    // `Of` is offered to a generic form control even though it has a required step: a control's opening
    // is what pins its value type, and `Label` says nothing about T, so without this a call site with no
    // starting value has no way in at all.
    [Fact]
    public void A_generic_control_with_a_required_step_still_opens_on_its_type_alone()
    {
        Assert.Contains("Of<T>()", Entries(Named), StringComparison.Ordinal);
    }

    // The shared Element/Component surface is emitted ONCE. It used to be emitted per chain shape, and
    // emitting more than one now is CS0111 — with the component as the receiver all the shapes collapse
    // to the same signature — so this count is load-bearing rather than tidiness.
    [Fact]
    public void The_shared_surface_is_emitted_once_for_a_member()
    {
        var output = Setters("""
                             namespace Rask.Core;
                             public abstract partial class Element : Component
                             {
                                 public string? Class { get; set; }
                             }
                             """);

        // `T` constrained to Element, not Element itself: the generic self-type is what lets one emission
        // hand back exactly the type it was called on, which is what replaced the four shapes.
        Assert.Contains(
            "public static T Class<T>(this T __b, string? value) where T : global::Rask.Core.Element",
            output, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(output, " Class<T>(this T __b"));
    }

    // The machinery is gone rather than merely unused. A phantom mode left on the intermediate states
    // is exactly how the last of it survived a refactor that had already stopped reading it.
    [Theory]
    [InlineData("TMode")]
    [InlineData("Rask.Core.Forms.Bound")]
    [InlineData("Rask.Core.Forms.Controlled")]
    public void No_trace_of_the_mode_types_survives_in_the_surface(string gone)
    {
        foreach (var source in new[] { Widget, Flag, Named })
        {
            Assert.DoesNotContain(gone, Entries(source), StringComparison.Ordinal);
            Assert.DoesNotContain(gone, Setters(source), StringComparison.Ordinal);
        }
    }

    // The body of one entry step: from its signature to the `return`, which is where the runtime calls
    // that decide folding live.
    private static List<string> Opening(string output, string step) =>
        output.Split('\n')
            .SkipWhile(l => !l.Contains(step, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(l => !l.Contains("return", StringComparison.Ordinal))
            .ToList();

    private static int Occurrences(string haystack, string needle)
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

    private static string Setters(string source) =>
        BuilderGeneratorHarness.Run(source).Source("RaskBuilderSetters.g.cs");

    private static string Entries(string source) =>
        BuilderGeneratorHarness.Run(source).Source("RaskBuilderEntryHost.g.cs");
}
