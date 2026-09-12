using Rask.Generators;

namespace Rask.Data.Generators.Tests;

/// <summary>
/// A component whose props are typed as a Rask.Data entity's generated model — the edit form's shape — driven
/// through the model, component-factory and routes generators in ONE run.
/// </summary>
/// <remarks>
/// <para>
/// To the factory generator <c>ProductModel</c> is an unresolved error type, which displays as the bare name
/// the author wrote. That name binds in the author's file, which has <c>using Shop.Catalog;</c>, and not in
/// <c>RaskBuilderSetters.g.cs</c>, which does not — so only a compilation holding all three outputs shows
/// whether the setters the chain calls exist.
/// </para>
/// <para>
/// Here rather than in Rask.Generators.Tests because the model generator's output needs Rask.Data and EF
/// Core, and that suite builds hundreds of compilations that would each pay for them.
/// </para>
/// </remarks>
public class GeneratedModelInComponentsTests
{
    private static readonly Dictionary<string, string> BuilderSurface = new()
    {
        ["build_property.RaskBuilderSurface"] = "true",
    };

    [Fact]
    public void A_component_in_another_namespace_takes_generated_model_props_by_their_full_name()
    {
        var run = GeneratorHarness.Run(
            """
            using System;
            using System.Collections.Generic;
            using Rask.Core;
            using Rask.Core.Routing;
            using Rask.Data;
            using Shop.Catalog;

            namespace Shop.Catalog
            {
                public sealed class Product : Model<Guid>
                {
                    private Product() { }
                    public string Name { get; private set; } = "";
                }
            }

            namespace Shop.Pages
            {
                public partial class Layout : Component { }

                [Route("/products/edit")]
                public partial class EditProduct : Component
                {
                    public ProductModel Edited { get; set; }
                    public ProductModel? Draft { get; set; }
                    public List<ProductModel>? Related { get; set; }
                    public Callback<ProductModel> OnSave { get; set; }
                }
            }
            """,
            [new ModelInputGenerator(), new ComponentFactoryGenerator(), new RoutesGenerator()],
            BuilderSurface,
            "Rask.Data", "Rask.Cqrs", "Rask.Core", "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.GeneratedCompileErrors());

        var setters = run.GeneratedSource("RaskBuilderSetters");
        Assert.Contains("global::Shop.Catalog.ProductModel value)", setters, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Catalog.ProductModel? value)", setters, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.List<global::Shop.Catalog.ProductModel>? value)",
            setters,
            StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Callback<global::Shop.Catalog.ProductModel>", setters, StringComparison.Ordinal);

        // The bare name, anywhere a generator other than the model's own wrote it.
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(r => r.GeneratedSources)
                .Where(s => !s.HintName.Contains("ProductModel", StringComparison.Ordinal)),
            s => s.SourceText.ToString().Contains("<ProductModel", StringComparison.Ordinal)
                 || s.SourceText.ToString().Contains(" ProductModel", StringComparison.Ordinal));
    }
}
