using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class PluralizerTests
{
    [Theory]
    [InlineData("Product", "Products")]
    [InlineData("Category", "Categories")]
    [InlineData("Box", "Boxes")]
    [InlineData("Dish", "Dishes")]
    [InlineData("Day", "Days")]     // vowel + y → just s
    [InlineData("Order", "Orders")]
    public void Pluralize(string singular, string plural) =>
        Assert.Equal(plural, Pluralizer.Pluralize(singular));
}

public sealed class FieldSpecParserTests
{
    [Fact]
    public void Parses_name_type_pairs()
    {
        Assert.True(FieldSpecParser.TryParse("Name:string, Price:decimal, InStock:bool", out var fields, out var error));

        Assert.Null(error);
        Assert.Collection(fields,
            f => { Assert.Equal("Name", f.Name); Assert.Equal("string", f.CsType); Assert.Equal("= \"\"", f.Initializer); },
            f => { Assert.Equal("Price", f.Name); Assert.Equal("decimal", f.CsType); Assert.Null(f.Initializer); },
            f => { Assert.Equal("InStock", f.Name); Assert.Equal("bool", f.CsType); Assert.Null(f.Initializer); });
    }

    [Theory]
    [InlineData("text", "string")]
    [InlineData("number", "int")]
    [InlineData("money", "decimal")]
    [InlineData("date", "DateTime")]
    [InlineData("guid", "Guid")]
    public void Maps_type_aliases(string alias, string csType)
    {
        Assert.True(FieldSpecParser.TryParse($"Field:{alias}", out var fields, out _));
        Assert.Equal(csType, fields[0].CsType);
    }

    [Fact]
    public void Rejects_unknown_type()
    {
        Assert.False(FieldSpecParser.TryParse("Name:blob", out _, out var error));
        Assert.Contains("Unknown field type 'blob'", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_malformed_pair()
    {
        Assert.False(FieldSpecParser.TryParse("Name", out _, out var error));
        Assert.Contains("name:type", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_explicit_Id_field()
    {
        Assert.False(FieldSpecParser.TryParse("Id:int", out _, out var error));
        Assert.Contains("added automatically", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_empty_spec()
    {
        Assert.False(FieldSpecParser.TryParse("  ", out _, out var error));
        Assert.Contains("At least one field", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_duplicate_field_names()
    {
        Assert.False(FieldSpecParser.TryParse("Name:string,Name:int", out _, out var error));
        Assert.Contains("Duplicate field 'Name'", error!, StringComparison.Ordinal);
    }
}

public sealed class FeatureGeneratorTests
{
    private static readonly IReadOnlyList<FieldSpec> Fields =
    [
        new("Name", "string", "= \"\""),
        new("Price", "decimal", null),
    ];

    private static ScaffoldResult Generate(string? context = null, string? plural = null) =>
        FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Product", Fields, context, plural, outputOverride: null);

    [Fact]
    public void Plural_override_drives_names_and_route()
    {
        var result = FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Person",
            [new FieldSpec("Name", "string", "= \"\"")], contextOverride: null, pluralOverride: "People", outputOverride: null);

        Assert.Contains(result.Files, f => f.Path.EndsWith("PeoplePage.cs", StringComparison.Ordinal));
        Assert.Contains("[Route(\"/people\")]", result.Files.Single(f => f.Path.EndsWith("PeoplePage.cs", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Bool_field_renders_a_bootstrap_checkbox()
    {
        var result = FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Task",
            [new FieldSpec("Done", "bool", null)], contextOverride: null, pluralOverride: null, outputOverride: null);

        var create = result.Files.Single(f => f.Path.EndsWith("CreateTaskPage.cs", StringComparison.Ordinal)).Content;
        Assert.Contains("form-check-input", create, StringComparison.Ordinal);
        Assert.DoesNotContain("Input(() => _item.Done, Id: \"done\", Class: \"form-control\")", create, StringComparison.Ordinal);
    }

    [Fact]
    public void Generates_five_files_with_a_local_context_by_default()
    {
        var result = Generate();

        var names = result.Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["CreateProductPage.cs", "EditProductPage.cs", "Product.cs", "ProductsDbContext.cs", "ProductsPage.cs"],
            names);
    }

    [Fact]
    public void Everything_lands_in_the_plural_feature_folder_with_its_namespace()
    {
        var result = Generate();

        Assert.All(result.Files, f => Assert.Equal(Path.GetFullPath("/proj/Features/Products"), Path.GetFullPath(Path.GetDirectoryName(f.Path)!)));
        var entity = result.Files.Single(f => f.Path.EndsWith("Product.cs", StringComparison.Ordinal));
        Assert.Contains("namespace MyApp.Features.Products;", entity.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Entity_has_an_id_and_a_property_per_field()
    {
        var entity = Generate().Files.Single(f => f.Path.EndsWith("Product.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public int Id { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; } = \"\";", entity, StringComparison.Ordinal);
        Assert.Contains("public decimal Price { get; set; }", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("{ get; set; };", entity, StringComparison.Ordinal); // no stray semicolon
    }

    [Fact]
    public void Pages_use_type_safe_routes_not_string_paths()
    {
        var list = Generate().Files.Single(f => f.Path.EndsWith("ProductsPage.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("NavLink(Routes.CreateProductPage()", list, StringComparison.Ordinal);
        Assert.Contains("Routes.EditProductPage(x.Id)", list, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/products\")]", list, StringComparison.Ordinal); // route attribute keeps the literal
    }

    [Fact]
    public void Default_next_steps_register_the_generated_context()
    {
        var result = Generate();

        Assert.Contains("AddDbContextFactory<ProductsDbContext>", result.Notes!, StringComparison.Ordinal);
        Assert.Contains("dotnet ef migrations add AddProduct", result.Notes!, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_context_skips_the_dbcontext_file_and_tells_you_to_add_a_dbset()
    {
        var result = Generate(context: "AppDbContext");

        Assert.DoesNotContain(result.Files, f => f.Path.EndsWith("DbContext.cs", StringComparison.Ordinal));
        Assert.Contains("IDbContextFactory<AppDbContext>", result.Files.First(f => f.Path.EndsWith("ProductsPage.cs", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
        Assert.Contains("public DbSet<Product> Products => Set<Product>();", result.Notes!, StringComparison.Ordinal);
    }
}
