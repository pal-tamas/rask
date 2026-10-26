namespace Rask.Batteries.Generators.Tests;

/// <summary>Finding the app's one <c>IRaskModel</c> and naming it to the model registry.</summary>
public class RaskModelGeneratorTests
{
    private const string Registry = """
        namespace Rask.Data
        {
            public interface IRaskModel
            {
                static virtual void Conventions(Microsoft.EntityFrameworkCore.ModelConfigurationBuilder c) { }
                static virtual void Model(Microsoft.EntityFrameworkCore.ModelBuilder m) { }
            }

            public static class ModelRegistry
            {
                public static void UseModel<TModel>() where TModel : IRaskModel { }
            }
        }
        """;

    [Fact]
    public void The_one_model_class_is_named_to_the_registry()
    {
        var run = GeneratorHarness.Run(
            Registry + """

            namespace Ta
            {
                public sealed class TaModel : Rask.Data.IRaskModel
                {
                    public static void Model(Microsoft.EntityFrameworkCore.ModelBuilder m) => m.Ignore<string>();
                }
            }
            """,
            new RaskModelGenerator(),
            "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "global::Rask.Data.ModelRegistry.UseModel<global::Ta.TaModel>();",
            run.GeneratedSource("__RaskModel"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_model_classes_are_RASK110_and_wire_neither()
    {
        var run = GeneratorHarness.Run(
            Registry + """

            namespace Ta
            {
                public sealed class TaModel : Rask.Data.IRaskModel { }
                public sealed class OtherModel : Rask.Data.IRaskModel { }
            }
            """,
            new RaskModelGenerator(),
            "Microsoft.EntityFrameworkCore");

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK110");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(run.HasGeneratedSource("__RaskModel"));
    }

    [Fact]
    public void An_app_without_a_model_class_gets_nothing_generated()
    {
        var run = GeneratorHarness.Run(
            Registry + """

            namespace Ta
            {
                public abstract class BaseModel : Rask.Data.IRaskModel { }
                public sealed class Customer : System.Exception { }
            }
            """,
            new RaskModelGenerator(),
            "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.Diagnostics);
        Assert.False(run.HasGeneratedSource("__RaskModel"));
    }
}
