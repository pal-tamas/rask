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
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

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
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

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
                      public static Widget Bound(string? Label = null, params IEnumerable<Component> Children)
                          => new();
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains(
            "string? Label = null, params global::System.Collections.Generic.IEnumerable<global::Rask.Core.Component> Children",
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
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

        // No `<...>` after the component name; forwarder body delegates to the source method.
        Assert.Contains("public static global::Demo.Widget Widget(string? Class = null)", output);
        Assert.Contains("=> global::Demo.Widget.Convenience(Class);", output);
        // Forwarders also carry the debugger-skip attribute (keeps stepping out of generated code).
        Assert.Contains("[global::System.Diagnostics.DebuggerStepThrough]", output);
    }

    [Fact]
    public void ValidatorForwarder_FansIntoNoneSyncAsyncOverloads()
    {
        var src = """
                  using System;
                  using System.Linq.Expressions;
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [GenerateForwarderFactory(Validator = "Validate")]
                      public static Widget Bound<TProp>(
                          Expression<Func<TProp>> Bind, Delegate? Validate = null, string? Class = null)
                          => new() { Class = Class };
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Generated.g.cs");

        // None overload: validator parameter omitted; forwarded as null (by name).
        Assert.Contains(
            "public static global::Demo.Widget Widget<TProp>(global::System.Linq.Expressions.Expression<global::System.Func<TProp>> Bind, string? Class = null)",
            output);
        Assert.Contains("=> global::Demo.Widget.Bound<TProp>(Bind: Bind, Validate: null, Class: Class);", output);

        // Sync overload: typed Validate<TProp>, required (no default), right after Bind.
        Assert.Contains("global::Rask.Core.Forms.Validate<TProp> Validate, string? Class = null)", output);
        // Async overload: typed ValidateAsync<TProp>.
        Assert.Contains("global::Rask.Core.Forms.ValidateAsync<TProp> Validate, string? Class = null)", output);
        Assert.Contains("=> global::Demo.Widget.Bound<TProp>(Bind: Bind, Validate: Validate, Class: Class);", output);
    }
}
