using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class ComponentGeneratorTests
{
    [Fact]
    public void Generates_under_features_shared_with_folder_namespace()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = ComponentGenerator.Generate(project, "/proj", "PriceTag", feature: null, outputOverride: null);

        Assert.Equal(Path.GetFullPath("/proj/Features/Shared/PriceTag.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Features.Shared;", file.Content, StringComparison.Ordinal);
        Assert.Contains("public sealed class PriceTag : Component", file.Content, StringComparison.Ordinal);
        Assert.EndsWith("\n", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_co_locates_into_that_slice()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = ComponentGenerator.Generate(project, "/proj", "OrderRow", feature: "Orders", outputOverride: null);

        Assert.Equal(Path.GetFullPath("/proj/Features/Orders/OrderRow.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Features.Orders;", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_override_changes_directory_and_namespace()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = ComponentGenerator.Generate(project, "/proj", "PriceTag", feature: null, outputOverride: "Widgets/Money");

        Assert.Equal(Path.GetFullPath("/proj/Widgets/Money/PriceTag.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Widgets.Money;", file.Content, StringComparison.Ordinal);
    }
}

public sealed class PageGeneratorTests
{
    [Theory]
    [InlineData("Products", "Products")]
    [InlineData("ProductsPage", "Products")]
    [InlineData("Page", "Page")]
    public void FeatureNameOf_strips_a_trailing_page_suffix(string name, string expected) =>
        Assert.Equal(expected, PageGenerator.FeatureNameOf(name));

    [Theory]
    [InlineData("products", "/products")]
    [InlineData("/products", "/products")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void NormalizeRoute_ensures_leading_slash(string? input, string? expected) =>
        Assert.Equal(expected, PageGenerator.NormalizeRoute(input));

    [Fact]
    public void Generates_routed_page_under_features_folder()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = PageGenerator.Generate(project, "/proj", "Products", route: null, outputOverride: null);

        Assert.Equal(Path.GetFullPath("/proj/Features/Products/ProductsPage.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("namespace MyApp.Features.Products;", file.Content, StringComparison.Ordinal);
        Assert.Contains("using Rask.Core.Routing;", file.Content, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/products\")]", file.Content, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProductsPage : Component", file.Content, StringComparison.Ordinal);
        Assert.Contains("Head => Title()[\"Products\"]", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Degenerate_page_name_does_not_double_the_suffix()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = PageGenerator.Generate(project, "/proj", "Page", route: null, outputOverride: null);

        Assert.Equal(Path.GetFullPath("/proj/Features/Page/Page.cs"), Path.GetFullPath(file.Path));
        Assert.Contains("public sealed class Page : Component", file.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("PagePage", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_route_wins_and_is_slash_prefixed()
    {
        var project = new ProjectContext("/proj", "MyApp");

        var file = PageGenerator.Generate(project, "/proj", "Dashboard", route: "admin/home", outputOverride: null);

        Assert.Contains("[Route(\"/admin/home\")]", file.Content, StringComparison.Ordinal);
    }
}
