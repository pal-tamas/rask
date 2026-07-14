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

    [Fact]
    public void Optional_field_is_nullable_with_no_initializer()
    {
        Assert.True(FieldSpecParser.TryParse("Note:string?", out var fields, out _));

        Assert.True(fields[0].IsNullable);
        Assert.Equal("string?", fields[0].PropertyType);
        Assert.Null(fields[0].Initializer);
    }

    [Fact]
    public void String_gets_a_default_max_length()
    {
        Assert.True(FieldSpecParser.TryParse("Name:string", out var fields, out _));
        Assert.Equal(FieldSpecParser.DefaultStringMaxLength, fields[0].MaxLength);
    }

    [Fact]
    public void String_max_length_can_be_overridden()
    {
        Assert.True(FieldSpecParser.TryParse("Note:string?(500)", out var fields, out _));
        Assert.Equal(500, fields[0].MaxLength);
        Assert.True(fields[0].IsNullable);
    }

    [Fact]
    public void Length_on_a_non_string_is_rejected()
    {
        Assert.False(FieldSpecParser.TryParse("Price:decimal(2)", out _, out var error));
        Assert.Contains("only applies to string", error!, StringComparison.Ordinal);
    }
}

public sealed class FeatureGeneratorTests
{
    private static readonly IReadOnlyList<FieldSpec> Fields =
    [
        new("Name", "string", IsNullable: false, MaxLength: 200),
        new("Price", "decimal", IsNullable: false, MaxLength: null),
    ];

    private static ScaffoldResult Generate(string idType = "Guid", string validation = "valueobjects", bool useBs = false, string? context = null, string? plural = null) =>
        FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Product", Fields, idType, validation, useBs, context, plural, outputOverride: null);

    private static string File(ScaffoldResult result, string fileName) =>
        result.Files.Single(f => Path.GetFileName(f.Path) == fileName).Content;

    [Fact]
    public void Generates_the_full_cqrs_slice_as_vertical_slice_files()
    {
        var names = Generate().Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "CreateProduct.cs", "DeleteProduct.cs", "Product.cs", "ProductConfiguration.cs",
                "ProductName.cs", "ProductRequest.cs", "ProductsDbContext.cs", "ProductsPage.cs", "UpdateProduct.cs",
            ],
            names);
    }

    [Fact]
    public void Everything_lands_in_the_plural_feature_folder_with_its_namespace()
    {
        var result = Generate();

        Assert.All(result.Files, f => Assert.Equal(Path.GetFullPath("/proj/Features/Products"), Path.GetFullPath(Path.GetDirectoryName(f.Path)!)));
        Assert.Contains("namespace MyApp.Features.Products;", File(result, "Product.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Entity_is_encapsulated_with_create_update_and_a_guid_id_by_default()
    {
        var entity = File(Generate(), "Product.cs");

        Assert.Contains("public Guid Id { get; private set; } = Guid.NewGuid();", entity, StringComparison.Ordinal);
        // Create/Update take primitives; the required string becomes a value object, wrapped via Create.
        Assert.Contains("public static Product Create(string name, decimal price) => new(ProductName.Create(name), price);", entity, StringComparison.Ordinal);
        Assert.Contains("public ProductName Name { get; private set; }", entity, StringComparison.Ordinal);
        Assert.Contains("this.Name = ProductName.Create(name);", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("{ get; set; }", entity, StringComparison.Ordinal); // all encapsulated
        Assert.DoesNotContain("DataAnnotations", entity, StringComparison.Ordinal); // schema lives in the EF config
    }

    [Fact]
    public void Required_string_becomes_a_value_object_with_built_in_validation()
    {
        var result = Generate();

        var vo = File(result, "ProductName.cs");
        Assert.Contains("public readonly record struct ProductName", vo, StringComparison.Ordinal);
        Assert.Contains("public const int MaxLength = 200;", vo, StringComparison.Ordinal);
        Assert.Contains("public static IEnumerable<string> Validate(string value)", vo, StringComparison.Ordinal);
        Assert.Contains("public static ProductName Create(string value)", vo, StringComparison.Ordinal);
        // The form wires the value object's Validate into the bound input.
        Assert.Contains("Input(() => _form.Name, Validate: ProductName.Validate", File(result, "CreateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Generates_an_ef_configuration_that_maps_the_schema()
    {
        var config = File(Generate(), "ProductConfiguration.cs");

        Assert.Contains("public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>", config, StringComparison.Ordinal);
        Assert.Contains("entity.HasKey(x => x.Id);", config, StringComparison.Ordinal);
        // The value object maps through its converter.
        Assert.Contains("entity.Property(x => x.Name).HasConversion(v => v.Value, s => ProductName.Create(s)).HasMaxLength(ProductName.MaxLength);", config, StringComparison.Ordinal);
        Assert.Contains("ApplyConfigurationsFromAssembly", File(Generate(), "ProductsDbContext.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Assignments_are_this_qualified_so_a_lowercase_field_does_not_self_assign()
    {
        var entity = FeatureGenerator.RenderEntity("MyApp.Features.Notes", "Note",
            [new FieldSpec("title", "string", false, 200)], "Guid", useValueObjects: false);

        Assert.Contains("this.title = title;", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("\n        title = title;", entity, StringComparison.Ordinal); // not a self-assignment
    }

    [Theory]
    [InlineData("int", "public int Id { get; private set; }", "{id:int}")]
    [InlineData("long", "public long Id { get; private set; }", "{id:long}")]
    public void Id_type_is_configurable(string idType, string idProp, string routeConstraint)
    {
        var result = Generate(idType);

        Assert.Contains(idProp, File(result, "Product.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid()", File(result, "Product.cs"), StringComparison.Ordinal);
        Assert.Contains(routeConstraint, File(result, "UpdateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Request_is_a_shared_plain_form_model()
    {
        var request = File(Generate(), "ProductRequest.cs");

        Assert.Contains("public sealed class ProductRequest", request, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; } = \"\";", request, StringComparison.Ordinal);
        Assert.DoesNotContain("[Required]", request, StringComparison.Ordinal); // validation lives on the value object
        // Both slices bind the same shared request — no Create/Update-specific request types.
        Assert.Contains("CreateProductCommand(ProductRequest Request)", File(Generate(), "CreateProduct.cs"), StringComparison.Ordinal);
        Assert.Contains("UpdateProductCommand(Guid Id, ProductRequest Request)", File(Generate(), "UpdateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_slice_holds_its_command_handler_and_page()
    {
        var slice = File(Generate(), "CreateProduct.cs");

        Assert.Contains("public sealed record CreateProductCommand(ProductRequest Request) : ICommand<Guid>", slice, StringComparison.Ordinal);
        Assert.Contains("ICommandHandler<CreateProductCommand, Guid>", slice, StringComparison.Ordinal);
        Assert.Contains("Product.Create(command.Request.Name, command.Request.Price)", slice, StringComparison.Ordinal);
        Assert.Contains("public sealed class CreateProduct(IDispatcher dispatcher, Navigator navigator) : Component", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_is_a_reusable_component_with_its_command()
    {
        var slice = File(Generate(), "DeleteProduct.cs");

        Assert.Contains("public sealed record DeleteProductCommand(Guid Id) : ICommand", slice, StringComparison.Ordinal);
        Assert.Contains("public sealed class DeleteProduct(IDispatcher dispatcher) : Component", slice, StringComparison.Ordinal);
        Assert.Contains("public Func<Task>? OnDeleted { get; set; }", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void List_page_dispatches_queries_and_renders_the_delete_component()
    {
        var list = File(Generate(), "ProductsPage.cs");

        Assert.Contains("public sealed class ProductsPage(IDispatcher dispatcher) : Component", list, StringComparison.Ordinal);
        Assert.Contains("dispatcher.DispatchAsync(new ListProductsQuery()", list, StringComparison.Ordinal);
        Assert.Contains("DeleteProduct(Id: x.Id, OnDeleted: LoadAsync)", list, StringComparison.Ordinal);
        Assert.Contains("NavLink(Routes.CreateProduct()", list, StringComparison.Ordinal);
        Assert.Contains("Routes.UpdateProduct(x.Id)", list, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/products\")]", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Bs_mode_uses_rask_bootstrap_components_and_utility_classes()
    {
        var result = Generate(useBs: true);

        var create = File(result, "CreateProduct.cs");
        Assert.Contains("BsCard(Class: Bs.Join(Shadow.Sm", create, StringComparison.Ordinal);
        Assert.Contains("BsInput(() => _form.Name, Validate: ProductName.Validate, Id: \"name\", Label: \"Name\")", create, StringComparison.Ordinal);
        Assert.Contains("BsButton(Type: \"submit\", Color: BsColor.Primary)", create, StringComparison.Ordinal);
        Assert.Contains("Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3))", create, StringComparison.Ordinal);

        var list = File(result, "ProductsPage.cs");
        Assert.Contains("BsTable(Striped: true, Hover: true, Responsive: true)", list, StringComparison.Ordinal);
        Assert.Contains("BsButton(Color: BsColor.Primary", list, StringComparison.Ordinal);
        Assert.Contains("BsIcon(Name: BsIconName.PlusLg", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Plural_override_drives_names_and_route()
    {
        var result = FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Person",
            Fields, "Guid", "valueobjects", useBs: false, contextOverride: null, pluralOverride: "People", outputOverride: null);

        Assert.Contains(result.Files, f => f.Path.EndsWith("PeoplePage.cs", StringComparison.Ordinal));
        Assert.Contains("[Route(\"/people\")]", File(result, "PeoplePage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Bool_field_renders_a_bootstrap_checkbox_not_a_text_input()
    {
        var result = FeatureGenerator.Generate(new ProjectContext("/proj", "MyApp"), "/proj", "Job",
            [new FieldSpec("Done", "bool", false, null)], "Guid", "valueobjects", useBs: false, null, null, outputOverride: null);

        var createJob = File(result, "CreateJob.cs");
        Assert.Contains("form-check-input", createJob, StringComparison.Ordinal);
    }

    [Fact]
    public void DataAnnotations_mode_uses_a_poco_entity_attributes_and_the_validator()
    {
        var result = Generate(validation: "dataannotations");

        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) == "ProductName.cs"); // no value object
        Assert.Contains("public string Name { get; private set; } = \"\";", File(result, "Product.cs"), StringComparison.Ordinal);
        var request = File(result, "ProductRequest.cs");
        Assert.Contains("[Required]", request, StringComparison.Ordinal);
        Assert.Contains("[MaxLength(200)]", request, StringComparison.Ordinal);
        Assert.Contains("DataAnnotationsValidator(),", File(result, "CreateProduct.cs"), StringComparison.Ordinal);
        Assert.Contains("entity.Property(x => x.Name).IsRequired().HasMaxLength(200);", File(result, "ProductConfiguration.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Fluent_mode_generates_a_validator_and_wires_it()
    {
        var result = Generate(validation: "fluent");

        var validator = File(result, "ProductRequestValidator.cs");
        Assert.Contains("public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>", validator, StringComparison.Ordinal);
        Assert.Contains("RuleFor(x => x.Name).NotEmpty().MaximumLength(200);", validator, StringComparison.Ordinal);
        Assert.Contains("FluentValidationValidator(new ProductRequestValidator()),", File(result, "CreateProduct.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) == "ProductName.cs"); // POCO, no value object
    }

    [Fact]
    public void Default_next_steps_register_cqrs_the_context_and_the_ef_design_package()
    {
        var notes = Generate().Notes!;

        Assert.Contains("AddRaskCqrs();", notes, StringComparison.Ordinal);
        Assert.Contains("AddDbContextFactory<ProductsDbContext>", notes, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", notes, StringComparison.Ordinal);
        Assert.Contains("dotnet ef migrations add AddProduct", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_context_skips_the_dbcontext_file_and_wires_handlers_to_it()
    {
        var result = Generate(context: "AppDbContext");

        Assert.DoesNotContain(result.Files, f => f.Path.EndsWith("DbContext.cs", StringComparison.Ordinal));
        Assert.Contains("IDbContextFactory<AppDbContext>", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
        Assert.Contains("public DbSet<Product> Products => Set<Product>();", result.Notes!, StringComparison.Ordinal);
    }
}
