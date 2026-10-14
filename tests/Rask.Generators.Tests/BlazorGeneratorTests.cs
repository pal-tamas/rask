using Rask.Generators.Blazor;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <see cref="BlazorGenerator" />'s check on <c>[BlazorParameter]</c> — a mapping that names a parameter the
///     hosted component does not declare is RASK100, because the property stays a chain step that nothing writes —
///     and the parameter writer it emits for a property the island declares itself.
/// </summary>
/// <remarks>
///     The Blazor types are declared in the source itself. The generator matches them by name — it has to, it runs
///     where no <c>Compilation</c> can resolve them — so a stub is exactly what a real reference looks like to it.
/// </remarks>
public class BlazorGeneratorTests
{
    private const string Blazor =
        """
        namespace Microsoft.AspNetCore.Components
        {
            public sealed class ParameterAttribute : System.Attribute { }
        }

        namespace Rask.Blazor
        {
            public abstract class BlazorComponent<T>
            {
                protected virtual void WriteParameters(System.Collections.Generic.IDictionary<string, object?> into) { }
            }

            public sealed class BlazorParameterAttribute(string name) : System.Attribute
            {
                public string Name { get; } = name;
            }
        }

        namespace Lib
        {
            public class ChartBase
            {
                [Microsoft.AspNetCore.Components.Parameter] public string? Class { get; set; }
            }

            public class MudChart : ChartBase
            {
                [Microsoft.AspNetCore.Components.Parameter] public string? ChartSeries { get; set; }
                [Microsoft.AspNetCore.Components.Parameter] public int Count { get; set; }
                public string? NotAParameter { get; set; }
            }
        }
        """;

    [Fact]
    public void A_mapping_onto_a_parameter_the_component_does_not_declare_is_reported_as_RASK100()
    {
        var source = Island("""[Rask.Blazor.BlazorParameter("ChartSeris")] public string? Series { get; set; }""");

        var run = GeneratorHarness.Run(source, new BlazorGenerator());

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK100");
        var message = diagnostic.GetMessage();
        Assert.Contains("Chart.Series", message, StringComparison.Ordinal);
        Assert.Contains("ChartSeris", message, StringComparison.Ordinal);
        Assert.Contains("MudChart", message, StringComparison.Ordinal);
        Assert.Contains("spell it exactly", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mapping_onto_a_property_that_is_not_a_parameter_is_reported_as_RASK100()
    {
        var source = Island("""[Rask.Blazor.BlazorParameter("NotAParameter")] public string? Note { get; set; }""");

        var run = GeneratorHarness.Run(source, new BlazorGenerator());

        Assert.Single(run.Diagnostics, d => d.Id == "RASK100");
    }

    [Theory]
    [InlineData("ChartSeries", "Series")]
    [InlineData("Class", "Appearance")]
    public void A_mapping_onto_a_declared_or_inherited_parameter_is_not_reported(string parameter, string property)
    {
        var source = Island($$"""[Rask.Blazor.BlazorParameter("{{parameter}}")] public string? {{property}} { get; set; }""");

        var run = GeneratorHarness.Run(source, new BlazorGenerator());

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK100");
    }

    [Fact]
    public void A_non_nullable_value_type_the_island_declares_itself_compiles_and_is_always_passed_on()
    {
        // #1196: the writer used to test it with `is not null`, which is CS0037 on an int.
        var source = Island("public int Count { get; set; }");

        var run = GeneratorHarness.Run(source, new BlazorGenerator());

        Assert.Empty(run.GeneratedCompileErrors());
        var writer = run.GeneratedSource("Chart.Blazor");
        Assert.Contains("into[\"Count\"] = this.Count;", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Count is not null", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_value_type_the_island_declares_itself_is_still_omitted_while_null()
    {
        var source = Island("public int? Count { get; set; }");

        var run = GeneratorHarness.Run(source, new BlazorGenerator());

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("if (this.Count is not null)", run.GeneratedSource("Chart.Blazor"), StringComparison.Ordinal);
    }

    private static string Island(string member) =>
        Blazor + $$"""

            namespace App
            {
                public sealed partial class Chart : Rask.Blazor.BlazorComponent<Lib.MudChart>
                {
                    {{member}}
                }
            }
            """;
}
