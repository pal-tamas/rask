namespace Rask.Generators.Tests;

public class FactoryAttributeTests
{
    [Fact]
    public void Factory_DoesNotEmit_ChildrenParameter()
    {
        // Children is delivered via the `Component this[params Child[]]` indexer on Component,
        // not as a factory parameter. The generator must filter Children out of the signature
        // even though it's a public settable property on the base class.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Wrap : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("public static global::Demo.Wrap Wrap(", output);
        // No Children parameter — neither named, nor as the trailing params slot.
        Assert.DoesNotContain("Children", output);
        Assert.DoesNotContain("params ", output);
    }

    [Fact]
    public void FactoryGeneric_EmitsBothNonGenericAndGenericOverloads()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  [FactoryGeneric("TModel",
                      ModelProperty = "Model",
                      TypedDelegateProperties = new[] { "OnValidSubmit" })]
                  public sealed class FormLike : Component
                  {
                      public object? Model { get; set; }
                      public System.Delegate? OnValidSubmit { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        // Non-generic factory still emitted; takes object? Model + Delegate? OnValidSubmit.
        Assert.Contains("public static global::Demo.FormLike FormLike(", output);
        Assert.Contains("object? Model = null", output);
        Assert.Contains("global::System.Delegate? OnValidSubmit = null", output);

        // Generic overload: TModel Model + narrowed Action<TModel>?/Func<TModel,Task>? pair.
        Assert.Contains("public static global::Demo.FormLike FormLike<TModel>(", output);
        Assert.Contains("TModel Model", output);
        Assert.Contains("global::System.Action<TModel>? OnValidSubmit = null", output);
        Assert.Contains(
            "global::System.Func<TModel, global::System.Threading.Tasks.Task>? OnValidSubmitAsync = null",
            output);
        Assert.Contains("where TModel : class", output);

        // Body collapses the sync/async pair into a single Delegate? and forwards.
        Assert.Contains("(global::System.Delegate?)OnValidSubmit ?? OnValidSubmitAsync", output);
    }

    [Fact]
    public void RouteFactoryHelper_NotEmittedInConsumerCompilation()
    {
        // The Route<T> helpers are emitted only by the compilation that defines the
        // Rask.Core.Routing.Route record itself (i.e. Rask.Core). A consumer compilation
        // that merely references the Rask.Core assembly must NOT re-emit the helpers,
        // otherwise duplicate symbol errors would appear.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed class HomePage : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var hints = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToList();
        Assert.DoesNotContain(hints, h => h.Contains("RouteFactory", StringComparison.Ordinal));
    }

    [Fact]
    public void FactoryGeneric_HonorsCustomConstraint()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public interface IFoo { }
                  [FactoryGeneric("T", ModelProperty = "Model", Constraint = "global::Demo.IFoo")]
                  public sealed class Holder : Component
                  {
                      public object? Model { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Holder<T>(", output);
        Assert.Contains("where T : global::Demo.IFoo", output);
    }
}
