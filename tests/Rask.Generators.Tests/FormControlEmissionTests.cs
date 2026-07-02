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
                                      public Callback<int>? OnChange { get; set; }
                                      public CallbackAsync<int>? OnChangeAsync { get; set; }
                                      public Expression<Func<int>>? Bind { get; set; }
                                      public Validate<int>? Validate { get; set; }
                                      public ValidateAsync<int>? ValidateAsync { get; set; }
                                      public Action<int>? AfterBind { get; set; }
                                      public Func<int, Task>? AfterBindAsync { get; set; }
                                      public string? Label { get; set; }
                                      public override Component? Render() => this;
                                  }
                                  """;

    [Fact]
    public void EmitsControlledFactory_WithoutBoundMembers()
    {
        var output = GeneratorDriverFixture.Run(Widget).GeneratedSource("Demo.Generated.g.cs");

        // Controlled factory carries Value/OnChange…
        Assert.Contains("global::Demo.Widget Widget(int? Value = null", output);
        Assert.Contains("global::Rask.Core.Callback<int>? OnChange = null", output);
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
                      public Callback<ICollection<TItem>>? OnChange { get; set; }
                      public CallbackAsync<ICollection<TItem>>? OnChangeAsync { get; set; }
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
