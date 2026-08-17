namespace Rask.Generators.Tests;

// A component implementing IFormControl<T> gets two generator-synthesized factories: a controlled factory
// (Value/OnChange, excluding the bound members) and a Bind-first bound factory fanned into none/sync/async.
public class FormControlEmissionTests
{
    private const string Widget = """
                                  using System;
                                  using System.Linq.Expressions;
                                  using System.Threading.Tasks;
                                  using Rask.Core;
                                  using Rask.Core.Forms;
                                  namespace Demo;
                                  public sealed class Widget : Component, IFormControl<int>
                                  {
                                      public int? Value { get; set; }
                                      public Action<int>? OnChange { get; set; }
                                      public Func<int, Task>? OnChangeAsync { get; set; }
                                      public Expression<Func<int>>? Bind { get; set; }
                                      public Validate<int>? Validate { get; set; }
                                      public ValidateAsync<int>? ValidateAsync { get; set; }
                                      public Action<int>? AfterBind { get; set; }
                                      public Func<int, Task>? AfterBindAsync { get; set; }
                                      public bool? Checked { get; set; }
                                      public Action<string>? OnInput { get; set; }
                                      public Func<string, Task>? OnInputAsync { get; set; }
                                      public string? Label { get; set; }
                                      public override Component? Render() => this;
                                  }
                                  """;

    // The emitter writes each factory signature on one line, so a signature IS a line. Bound overloads are
    // the ones taking the Bind expression; everything else is the controlled factory. Splitting them is what
    // lets a DoesNotContain assertion mean "not a parameter of THIS factory" rather than "absent everywhere"
    // — the two factories deliberately share most of their parameter names.
    private static (List<string> Bound, List<string> Controlled) Signatures(string output)
    {
        var sigs = output.Split('\n').Where(l => l.Contains("Widget Widget(", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(sigs);
        var bound = sigs.Where(l => l.Contains("Func<int>> Bind", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, bound.Count); // none / sync / async validator fan-out
        return (bound, sigs.Except(bound).ToList());
    }

    [Fact]
    public void EmitsControlledFactory_WithoutBoundMembers()
    {
        var output = GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs");

        // Controlled factory carries Value/OnChange…
        Assert.Contains("global::Demo.Widget Widget(int? Value = null", output);
        Assert.Contains("Action<int>? OnChange = null", output);
        // …and never exposes the bound members as a controlled-factory parameter that defaults to null.
        Assert.DoesNotContain("global::Rask.Core.Forms.Validate<int>? Validate = null", output);
        Assert.DoesNotContain("Expression<global::System.Func<int>>? Bind = null", output);
    }

    [Fact]
    public void EmitsBoundFactory_BindFirst()
    {
        var output = GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains(
            "global::Demo.Widget Widget(global::System.Linq.Expressions.Expression<global::System.Func<int>> Bind",
            output);
    }

    [Fact]
    public void BoundFactory_FansValidatorIntoNoneSyncAsync()
    {
        var output = GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs");

        // Sync overload takes Validate<int>; async takes ValidateAsync<int> (param named Validate either way).
        Assert.Contains("global::Rask.Core.Forms.Validate<int> Validate", output);
        Assert.Contains("global::Rask.Core.Forms.ValidateAsync<int> Validate", output);
        // None overload sets both validator props to null.
        Assert.Contains("Validate = null", output);
        Assert.Contains("ValidateAsync = null", output);
    }

    [Fact]
    public void ControlledFactory_AutoWrapsOnChange()
    {
        var output = GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains("global::Rask.Core.AutoCallback.Wrap(OnChange)", output);
    }

    // Bind owns the value and the write-back handler, so the controlled props are not parameters of the
    // bound factory: setting them next to Bind used to compile and then be dropped at render time.
    [Theory]
    [InlineData("Value")]
    [InlineData("Checked")]
    [InlineData("OnChange")]
    [InlineData("OnChangeAsync")]
    [InlineData("OnInput")]
    [InlineData("OnInputAsync")]
    public void BoundFactory_DoesNotTakeControlledProps(string prop)
    {
        var (bound, controlled) = Signatures(GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs"));

        Assert.All(bound, sig => Assert.DoesNotContain(" " + prop + " =", sig, StringComparison.Ordinal));
        // Guard the assertion itself: the prop IS a controlled-factory parameter, so a typo in the name
        // would otherwise make the check above pass for the wrong reason.
        Assert.All(controlled, sig => Assert.Contains(" " + prop + " =", sig, StringComparison.Ordinal));
    }

    // The mirror rule: AfterBind/AfterBindAsync are post-bind hooks, so they exist only where Bind does.
    [Theory]
    [InlineData("AfterBind")]
    [InlineData("AfterBindAsync")]
    public void ControlledFactory_DoesNotTakeBoundProps(string prop)
    {
        var (bound, controlled) = Signatures(GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs"));

        Assert.All(controlled, sig => Assert.DoesNotContain(" " + prop + " =", sig, StringComparison.Ordinal));
        Assert.All(bound, sig => Assert.Contains(" " + prop + " =", sig, StringComparison.Ordinal));
    }

    // Everything that is neither mode's business stays on both factories — the exclusion is targeted, not a
    // blanket "the bound factory takes Bind and nothing else".
    [Fact]
    public void BothFactories_KeepSharedDisplayProps()
    {
        var (bound, controlled) = Signatures(GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs"));

        Assert.All(bound.Concat(controlled),
            sig => Assert.Contains("string? Label = null", sig, StringComparison.Ordinal));
    }

    [Fact]
    public void GenericFormControl_CarriesTypeParam_AndValueTypedValidator()
    {
        var src = """
                  using System;
                  using System.Collections.Generic;
                  using System.Linq.Expressions;
                  using System.Threading.Tasks;
                  using Rask.Core;
                  using Rask.Core.Forms;
                  namespace Demo;
                  public sealed class Picker<TItem> : Component, IFormControl<ICollection<TItem>>
                  {
                      public ICollection<TItem>? Value { get; set; }
                      public Action<ICollection<TItem>>? OnChange { get; set; }
                      public Func<ICollection<TItem>, Task>? OnChangeAsync { get; set; }
                      public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
                      public Validate<ICollection<TItem>>? Validate { get; set; }
                      public ValidateAsync<ICollection<TItem>>? ValidateAsync { get; set; }
                      public Action<ICollection<TItem>>? AfterBind { get; set; }
                      public Func<ICollection<TItem>, Task>? AfterBindAsync { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var output = GeneratorDriverFixture.Run(src).GeneratedSource("Demo.Generated.g.cs");

        // The bound factory carries the component's <TItem> and the validator closes over ICollection<TItem>.
        Assert.Contains(
            "global::Demo.Picker<TItem> Picker<TItem>(global::System.Linq.Expressions.Expression<global::System.Func<global::System.Collections.Generic.ICollection<TItem>>> Bind",
            output);
        Assert.Contains(
            "global::Rask.Core.Forms.Validate<global::System.Collections.Generic.ICollection<TItem>> Validate",
            output);
    }
}
