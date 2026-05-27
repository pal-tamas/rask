namespace Rask.Generators.Tests;

public class ForwarderFactoryEmissionTests
{
    [Fact]
    public void GenericMethodWithConstraint_EmitsMatchingForwarder()
    {
        var src = """
                  using System;
                  using System.Linq.Expressions;
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [GenerateForwarderFactory]
                      public static Widget Bound<TProp>(Expression<Func<TProp>> Bind, string? Class = null) where TProp : class
                          => new() { Class = Class };
                      public override RenderResult Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains(
            "public static global::Demo.Widget Widget<TProp>(global::System.Linq.Expressions.Expression<global::System.Func<TProp>> Bind, string? Class = null)",
            output);
        Assert.Contains("where TProp : class", output);
        Assert.Contains("=> global::Demo.Widget.Bound<TProp>(Bind, Class);", output);
    }

    [Fact]
    public void OptionalDefaults_RoundTrip()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [GenerateForwarderFactory]
                      public static Widget Bound(string? Name = null, bool Flag = false, int Count = 0)
                          => new();
                      public override RenderResult Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("string? Name = null", output);
        Assert.Contains("bool Flag = false", output);
        Assert.Contains("int Count = 0", output);
        Assert.Contains("=> global::Demo.Widget.Bound(Name, Flag, Count);", output);
    }

    [Fact]
    public void ParamsArray_RoundTrips()
    {
        var src = """
                  using System.Collections.Generic;
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [GenerateForwarderFactory]
                      public static Widget Bound(string? Label = null, params IEnumerable<Child> Children)
                          => new();
                      public override RenderResult Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains(
            "string? Label = null, params global::System.Collections.Generic.IEnumerable<global::Rask.Core.Child> Children",
            output);
        Assert.Contains("=> global::Demo.Widget.Bound(Label, Children);", output);
    }

    [Fact]
    public void NonGenericForwarder_EmittedWithoutTypeParameters()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [GenerateForwarderFactory]
                      public static Widget Convenience(string? Class = null) => new() { Class = Class };
                      public override RenderResult Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        // No `<...>` after the component name; forwarder body delegates to the source method.
        Assert.Contains("public static global::Demo.Widget Widget(string? Class = null)", output);
        Assert.Contains("=> global::Demo.Widget.Convenience(Class);", output);
    }
}
