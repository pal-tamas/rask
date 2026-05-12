namespace Rask.Generators.Tests;

public class ComponentFactoryGeneratorTests
{
    [Fact]
    public void NoPublicProperties_EmitsParameterlessFactory()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("public static global::Demo.Widget Widget()", output);
        Assert.Contains(
            "static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Demo.Widget>(__sp)",
            output);
    }

    [Fact]
    public void OneNonNullableProperty_AddsRequiredParam()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string Name)", output);
        Assert.DoesNotContain("Name = null", output);
        Assert.Contains("__c.Name = Name;", output);
    }

    [Fact]
    public void OneNullableProperty_AddsOptionalParamWithNullDefault()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? Subtitle { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string? Subtitle = null)", output);
        Assert.Contains("__c.Subtitle = Subtitle;", output);
    }

    [Fact]
    public void PropertyWithInitializer_NotAFactoryParam()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Tag { get; set; } = "x";
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget()", output);
        Assert.DoesNotContain("Tag", output);
    }

    [Fact]
    public void MixedProps_RequiredBeforeOptional()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public int? Age { get; set; }
                      public string Title { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        // Required before optional, declaration order within group.
        Assert.Contains("Widget(string Name, string Title, int? Age = null)", output);
    }

    [Fact]
    public void WithDIConstructor_UsesActivatorUtilities()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public interface IClock { }
                  public sealed class Widget(IClock clock) : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("ActivatorUtilities.CreateInstance<global::Demo.Widget>(__sp)", output);
        // No-context branch must throw, since DI ctor cannot be satisfied without an IServiceProvider.
        Assert.Contains("throw new global::System.InvalidOperationException", output);
        Assert.Contains("__c.Name = Name;", output);
    }

    [Fact]
    public void WithoutDIConstructor_UsesObjectInitializerInNoContextBranch()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        // No-context branch: closure-based GetOrCreate using object initializer.
        Assert.Contains("__c = new global::Demo.Widget()", output);
        // Trailing assignment for re-application on every render.
        Assert.Contains("__c.Name = Name;", output);
    }

    [Fact]
    public void PropsAppliedAfterGetOrCreate()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public interface IClock { }
                  public sealed class Widget(IClock clock) : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        var getOrCreateIdx = output.IndexOf("GetOrCreate", StringComparison.Ordinal);
        var assignmentIdx = output.IndexOf("__c.Name = Name;", StringComparison.Ordinal);
        Assert.True(getOrCreateIdx > 0 && assignmentIdx > getOrCreateIdx,
            "Property assignments must appear after GetOrCreate so cached instances get fresh values each render.");
    }

    [Fact]
    public void UserMarkedRequiredWithoutDI_NoRask002()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public required string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK002");
    }

    [Fact]
    public void UserMarkedRequiredWithDI_RaisesRask002()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public interface IClock { }
                  public sealed class Widget(IClock clock) : Component
                  {
                      public required string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK002");
    }

    [Fact]
    public void NonNullableNoDefault_RaisesRask001()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK001");
    }

    [Fact]
    public void RequiredKeyword_DoesNotRaiseRask001()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public required string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK001");
    }

    [Fact]
    public void SkipFactoryOnProperty_PropertyOmitted()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      [SkipFactory]
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget()", output);
        Assert.DoesNotContain("Name = Name", output);
    }

    [Fact]
    public void InitOnlySetter_Included()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; init; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string Name)", output);
    }

    [Fact]
    public void PrivateSetter_Excluded()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; private set; } = "";
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget()", output);
        Assert.DoesNotContain("Name = Name", output);
    }

    [Fact]
    public void StaticProperty_Excluded()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public static string Default { get; set; } = "";
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget()", output);
        Assert.DoesNotContain("Default = default", output);
    }

    [Fact]
    public void ValueTypeNonNullable_IsRequired()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public int Count { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(int Count)", output);
        Assert.DoesNotContain("Count = null", output);
    }

    [Fact]
    public void RoutingAttributes_DoNotChangeFactoryParamCasing()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/counter/{name?}")]
                  public sealed class CounterPage : Component
                  {
                      [RouteParam] public string? Name { get; set; }
                      [QueryParam] public string? Greeting { get; set; }
                      public string? Other { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("CounterPage(string? Name = null, string? Greeting = null, string? Other = null)", output);
    }

    [Fact]
    public void IncrementalCacheStability_RerunYieldsCachedCandidates()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        // First run.
        var first = GeneratorDriverFixture.Run(src);
        // Second run with the same input should re-use cached steps.
        var firstOutput = first.GeneratedSource("Demo.Components.g.cs");
        var second = GeneratorDriverFixture.Run(src);
        var secondOutput = second.GeneratedSource("Demo.Components.g.cs");

        // Outputs should be byte-identical when the inputs are identical.
        Assert.Equal(firstOutput, secondOutput);
    }

    [Fact]
    public void GenericComponentWithTypeParameter_EmitsGenericFactory()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Foo<TProp> : Component
                  {
                      public required TProp Value { get; init; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("public static global::Demo.Foo<TProp> Foo<TProp>(TProp Value)", output);
        Assert.Contains("new global::Demo.Foo<TProp>()", output);
        Assert.Contains("__c.Value = Value;", output);
    }

    [Fact]
    public void GenericComponent_PropertyOfExpressionType_RoundtripsTypeParameter()
    {
        var src = """
                  using System;
                  using System.Linq.Expressions;
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Bound<TProp> : Component
                  {
                      public required Expression<Func<TProp>> Bind { get; init; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Bound<TProp>(global::System.Linq.Expressions.Expression<global::System.Func<TProp>> Bind)",
            output);
    }

    [Fact]
    public void GenericComponent_WithClassConstraint_EmitsConstraintClause()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Card<T> : Component where T : class
                  {
                      public required T Model { get; init; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Card<T>(T Model) where T : class", output);
    }

    [Fact]
    public void ParameterlessFactory_CallsNotifyParameters_WithPropsChangedFalse()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("__ctx.NotifyParameters(__c, propsChanged: false);", output);
    }

    [Fact]
    public void FactoryWithProps_CallsNotifyParametersWithComputedFlag()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? Title { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        var titleIdx = output.IndexOf("__c.Title = Title;", StringComparison.Ordinal);
        var notifyIdx = output.IndexOf("NotifyParameters(__c, __propsChanged)", StringComparison.Ordinal);
        Assert.True(titleIdx > 0);
        Assert.True(notifyIdx > 0);
        Assert.True(notifyIdx > titleIdx, "NotifyParameters must follow trailing property assignments");
    }

    [Fact]
    public void FactoryWithProps_EmitsSnapshotBeforeAssignment()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? Title { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        var snapshotIdx = output.IndexOf("var __old_Title = __c.Title;", StringComparison.Ordinal);
        var assignmentIdx = output.IndexOf("__c.Title = Title;", StringComparison.Ordinal);
        Assert.True(snapshotIdx > 0, "snapshot of old value must be emitted");
        Assert.True(snapshotIdx < assignmentIdx, "snapshot must appear before the assignment");
    }

    [Fact]
    public void FactoryWithProps_EmitsEqualityComparerDiff()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? Title { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains(
            "!global::System.Collections.Generic.EqualityComparer<string?>.Default.Equals(__old_Title, Title)",
            output);
        Assert.Contains("var __propsChanged", output);
    }

    [Fact]
    public void FactoryWithMultipleProps_FoldsDiffWithOr()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? A { get; set; }
                      public string? B { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("__old_A, A)", output);
        Assert.Contains("__old_B, B)", output);
        Assert.Contains(" ||", output);
    }

    [Fact]
    public void GenericComponent_WithoutDIConstructor_UsesObjectInitializer()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Box<T> : Component
                  {
                      public required T Value { get; init; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        // No ActivatorUtilities path: object-initializer used in both context branches.
        Assert.DoesNotContain("ActivatorUtilities.CreateInstance<global::Demo.Box<T>>", output);
        Assert.Contains("new global::Demo.Box<T>()", output);
        // Trailing assignment refresh still happens.
        Assert.Contains("__c.Value = Value;", output);
    }

    [Fact]
    public void BaseClassProperty_NotFlattenedIntoDerivedFactory()
    {
        // GetMembers() returns only directly-declared members; base properties stay on the base's factory.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public abstract class BasePage : Component
                  {
                      public string? BaseProp { get; set; }
                  }
                  public sealed class Widget : BasePage
                  {
                      public string? DerivedProp { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string? DerivedProp = null)", output);
        Assert.DoesNotContain("BaseProp", output);
    }

    [Fact]
    public void AbstractPropertyOverriddenInDerived_AppearsOnceInDerivedFactory()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public abstract class BasePage : Component
                  {
                      public abstract string? Title { get; set; }
                  }
                  public sealed class Widget : BasePage
                  {
                      public override string? Title { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string? Title = null)", output);
        // No duplicate assignment.
        var firstAssign = output.IndexOf("__c.Title = Title;", StringComparison.Ordinal);
        Assert.True(firstAssign > 0);
        var nextAssign = output.IndexOf("__c.Title = Title;", firstAssign + 1, StringComparison.Ordinal);
        Assert.Equal(-1, nextAssign);
    }

    [Fact]
    public void NullableDisableContext_StringProperty_TreatedAsNonNullable()
    {
        // Under `#nullable disable` a `string` property has no Annotated marker, so the factory
        // treats it as required (no default value).
        var src = """
                  using Rask.Core;
                  #nullable disable
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains("Widget(string Name)", output);
        Assert.DoesNotContain("Name = null", output);
    }

    [Fact]
    public void TenProperties_FactoryParameterOrderMatchesDeclaration()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string? P00 { get; set; }
                      public string? P01 { get; set; }
                      public string? P02 { get; set; }
                      public string? P03 { get; set; }
                      public string? P04 { get; set; }
                      public string? P05 { get; set; }
                      public string? P06 { get; set; }
                      public string? P07 { get; set; }
                      public string? P08 { get; set; }
                      public string? P09 { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.Contains(
            "Widget(string? P00 = null, string? P01 = null, string? P02 = null, string? P03 = null, " +
            "string? P04 = null, string? P05 = null, string? P06 = null, string? P07 = null, " +
            "string? P08 = null, string? P09 = null)",
            output);
    }
}
