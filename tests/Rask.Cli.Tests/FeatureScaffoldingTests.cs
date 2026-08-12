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
    [InlineData("date", "DateOnly")]
    [InlineData("time", "TimeOnly")]
    [InlineData("datetime", "DateTime")]
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

    private static ScaffoldResult Generate(string idType = "Guid", string validation = "valueobjects", bool useBs = false, bool useModal = false, bool useSoftDelete = false, bool useConcurrency = false, bool useEvents = false, bool useOutbox = false, bool useTests = false, string? context = null, string? contextNamespace = null, string? plural = null) =>
        FeatureGenerator.Generate(
            new ProjectContext("/proj", "MyApp"),
            "/proj",
            new FeatureSpec(new EntitySpec("Product", plural ?? Pluralizer.Pluralize("Product"), Fields), []),
            new FeatureOptions
            {
                IdType = idType,
                Validation = validation,
                UseBs = useBs,
                UseModal = useModal,
                UseSoftDelete = useSoftDelete,
                UseConcurrency = useConcurrency,
                UseEvents = useEvents,
                UseOutbox = useOutbox,
                UseTests = useTests,
                ExistingContext = context,
                ContextNamespace = contextNamespace,
                OutputOverride = null,
            });

    private static string File(ScaffoldResult result, string fileName) =>
        result.Files.Single(f => Path.GetFileName(f.Path) == fileName).Content;

    [Fact]
    public void Generates_the_full_cqrs_slice_as_vertical_slice_files()
    {
        var names = Generate().Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "AppDbContext.cs", "CreateProduct.cs", "DeleteProduct.cs", "Product.cs", "ProductConfiguration.cs",
                "ProductName.cs", "ProductRequest.cs", "ProductsPage.cs", "UpdateProduct.cs",
            ],
            names);
    }

    [Fact]
    public void Everything_lands_in_the_plural_feature_folder_with_its_namespace()
    {
        var result = Generate();

        // The DbContext is the app's, not the feature's, so it lives in the cross-cutting Features/Shared bucket.
        Assert.All(
            result.Files.Where(f => Path.GetFileName(f.Path) != "AppDbContext.cs"),
            f => Assert.Equal(Path.GetFullPath("/proj/Features/Products"), Path.GetFullPath(Path.GetDirectoryName(f.Path)!)));
        Assert.Contains("namespace MyApp.Features.Products;", File(result, "Product.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_context_a_run_writes_is_the_shared_one_rask_new_data_scaffolds()
    {
        var result = Generate();

        var context = result.Files.Single(f => Path.GetFileName(f.Path) == "AppDbContext.cs");
        Assert.Equal(Path.GetFullPath("/proj/Features/Shared"), Path.GetFullPath(Path.GetDirectoryName(context.Path)!));
        Assert.Contains("namespace MyApp.Features.Shared;", context.Content, StringComparison.Ordinal);
        Assert.Contains("public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)", context.Content, StringComparison.Ordinal);
        // It sees the entities, which live in the feature's namespace, not its own.
        Assert.Contains("using MyApp.Features.Products;", context.Content, StringComparison.Ordinal);
        Assert.Contains("public DbSet<Product> Products => Set<Product>();", context.Content, StringComparison.Ordinal);
        // …and so do the handlers, which are in the feature and name the context.
        Assert.Contains("using MyApp.Features.Shared;", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
        Assert.Contains("IDbContextFactory<AppDbContext>", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Entity_is_encapsulated_with_create_update_and_a_guid_id_by_default()
    {
        var entity = File(Generate(), "Product.cs");

        // Id (and the audit stamps + domain-events buffer) come from the Rask.Data base; a Guid is assigned up front.
        Assert.Contains("public sealed class Product : Entity<Guid>", entity, StringComparison.Ordinal);
        Assert.Contains("Id = Guid.NewGuid();", entity, StringComparison.Ordinal);
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
        Assert.Contains("Input.Bind(() => _form.Name).Validate(ProductName.Validate).Id(\"name\")", File(result, "CreateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Generates_an_ef_configuration_that_maps_the_schema()
    {
        var config = File(Generate(), "ProductConfiguration.cs");

        Assert.Contains("public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>", config, StringComparison.Ordinal);
        Assert.Contains("entity.HasKey(x => x.Id);", config, StringComparison.Ordinal);
        // The value object maps through its converter.
        Assert.Contains("entity.Property(x => x.Name).HasConversion(v => v.Value, s => ProductName.Create(s)).HasMaxLength(ProductName.MaxLength);", config, StringComparison.Ordinal);
        Assert.Contains("ApplyConfigurationsFromAssembly", File(Generate(), "AppDbContext.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Assignments_are_this_qualified_so_a_lowercase_field_does_not_self_assign()
    {
        var entity = FeatureGenerator.RenderEntity("MyApp.Features.Notes", "Note",
            [new FieldSpec("title", "string", false, 200)], "Guid", useValueObjects: false, useSoftDelete: false, useConcurrency: false, useEvents: false);

        Assert.Contains("this.title = title;", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("\n        title = title;", entity, StringComparison.Ordinal); // not a self-assignment
    }

    [Theory]
    [InlineData("int", "Entity<int>", "{id:int}")]
    [InlineData("long", "Entity<long>", "{id:long}")]
    public void Id_type_is_configurable(string idType, string baseType, string routeConstraint)
    {
        var result = Generate(idType);

        // Id lives on the base; an int/long id is database-generated (no Guid.NewGuid()).
        Assert.Contains("public sealed class Product : " + baseType, File(result, "Product.cs"), StringComparison.Ordinal);
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
        Assert.Contains("public sealed partial class CreateProduct(IDispatcher dispatcher, Navigator navigator) : Component", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_is_a_reusable_component_with_its_command()
    {
        var slice = File(Generate(), "DeleteProduct.cs");

        Assert.Contains("public sealed record DeleteProductCommand(Guid Id) : ICommand", slice, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class DeleteProduct(IDispatcher dispatcher) : Component", slice, StringComparison.Ordinal);
        // A plain delegate. It briefly had to be a carrier: while the chain's receiver was the component,
        // a delegate-typed property was invocable and the step that sets it could not share its name, so
        // the scaffolded caller could not write `.OnDeleted(LoadAsync)`. The `Build<T>` receiver removed
        // that, and scaffolded code says what it means again.
        Assert.Contains("public Func<Task>? OnDeleted { get; set; }", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void List_page_dispatches_queries_and_renders_the_delete_component()
    {
        var list = File(Generate(), "ProductsPage.cs");

        Assert.Contains("public sealed partial class ProductsPage(IDispatcher dispatcher) : Component", list, StringComparison.Ordinal);
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
        Assert.Contains("BsCard.Class(Bs.Join(Shadow.Sm", create, StringComparison.Ordinal);
        Assert.Contains("BsInput.Bind(() => _form.Name).Validate(ProductName.Validate).Id(\"name\").Label(\"Name\")", create, StringComparison.Ordinal);
        Assert.Contains("BsButton.Type(\"submit\").Color(BsColor.Primary)", create, StringComparison.Ordinal);
        Assert.Contains("Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3))", create, StringComparison.Ordinal);

        var list = File(result, "ProductsPage.cs");
        Assert.Contains("BsTable.Striped(true).Hover(true).Responsive(true)", list, StringComparison.Ordinal);
        Assert.Contains("BsButton.Color(BsColor.Primary)", list, StringComparison.Ordinal);
        Assert.Contains("BsIcon.Name(BsIconName.PlusLg)", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Modal_mode_puts_crud_in_a_bsmodal_and_drops_the_separate_pages()
    {
        var result = Generate(useModal: true);

        // No separate Create/Update page files — they live on the list page.
        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) is "CreateProduct.cs" or "UpdateProduct.cs");

        var list = File(result, "ProductsPage.cs");
        Assert.Contains("BsModal.Open(_modalOpen)", list, StringComparison.Ordinal);
        Assert.Contains("private void OpenCreate()", list, StringComparison.Ordinal);
        Assert.Contains("private async Task OpenEditAsync(Guid id)", list, StringComparison.Ordinal);
        // The create + update CQRS lives on the list page now.
        Assert.Contains("public sealed record CreateProductCommand(ProductRequest Request)", list, StringComparison.Ordinal);
        Assert.Contains("public sealed record UpdateProductCommand(Guid Id, ProductRequest Request)", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_generated_entity_inherits_the_rask_data_base()
    {
        var entity = File(Generate(), "Product.cs");
        Assert.Contains("public sealed class Product : Entity<Guid>", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_always_loads_and_removes_so_interceptors_fire()
    {
        // ExecuteDelete would bypass SaveChanges + the soft-delete/audit/event interceptors.
        var delete = File(Generate(), "DeleteProduct.cs");
        Assert.Contains("db.Products.Remove(entity);", delete, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteDeleteAsync", delete, StringComparison.Ordinal);
    }

    [Fact]
    public void Soft_delete_makes_the_entity_ISoftDeletable_and_generates_restore_plus_toggle()
    {
        var result = Generate(useSoftDelete: true);

        var entity = File(result, "Product.cs");
        Assert.Contains("public sealed class Product : Entity<Guid>, ISoftDeletable", entity, StringComparison.Ordinal);
        Assert.Contains("public DateTime? DeletedAt { get; private set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public void Restore() => DeletedAt = null;", entity, StringComparison.Ordinal);

        // A reusable Restore button + command, mirroring Delete.
        var restore = File(result, "RestoreProduct.cs");
        Assert.Contains("public sealed record RestoreProductCommand(Guid Id) : ICommand;", restore, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", restore, StringComparison.Ordinal);
        Assert.Contains("entity.Restore();", restore, StringComparison.Ordinal);

        // The list page can show + restore deleted rows via a toggle.
        var list = File(result, "ProductsPage.cs");
        Assert.Contains("public sealed record ListProductsQuery(bool IncludeDeleted = false)", list, StringComparison.Ordinal);
        Assert.Contains("items = items.IgnoreQueryFilters();", list, StringComparison.Ordinal);
        Assert.Contains("private async Task ToggleDeletedAsync()", list, StringComparison.Ordinal);
        Assert.Contains("_showDeleted ? \"Hide deleted\" : \"Show deleted\"", list, StringComparison.Ordinal);
        Assert.Contains("x.DeletedAt is null ? (Component)DeleteProduct(Id: x.Id, OnDeleted: LoadAsync) : RestoreProduct(Id: x.Id, OnRestored: LoadAsync)", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_soft_delete_there_is_no_restore_file_or_deletedat()
    {
        var result = Generate();
        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) == "RestoreProduct.cs");
        Assert.DoesNotContain("DeletedAt", File(result, "Product.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("_showDeleted", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_dbcontext_applies_rask_conventions()
    {
        Assert.Contains("modelBuilder.ApplyRaskConventions();", File(Generate(), "AppDbContext.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrency_adds_a_version_token_that_round_trips_and_conflicts_gracefully()
    {
        var result = Generate(useConcurrency: true);

        // The entity opts into IVersioned; ApplyRaskConventions marks Version the concurrency token.
        var entity = File(result, "Product.cs");
        Assert.Contains("public sealed class Product : Entity<Guid>, IVersioned", entity, StringComparison.Ordinal);
        Assert.Contains("public int Version { get; private set; }", entity, StringComparison.Ordinal);

        // The request + edit form round-trip the original Version through a hidden field.
        Assert.Contains("public int Version { get; set; }", File(result, "ProductRequest.cs"), StringComparison.Ordinal);
        var update = File(result, "UpdateProduct.cs");
        Assert.Contains("Input.Bind(() => _form.Version).Type(InputType.Hidden)", update, StringComparison.Ordinal);
        Assert.Contains("_form.Version = entity.Version;", update, StringComparison.Ordinal);

        // The Update handler sets the original value; a conflict is caught + shown inline (not a raw error page).
        Assert.Contains("db.Entry(entity).Property(x => x.Version).OriginalValue = command.Request.Version;", update, StringComparison.Ordinal);
        Assert.Contains("catch (DbUpdateConcurrencyException)", update, StringComparison.Ordinal);
        Assert.Contains("This record changed since you opened it", update, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_concurrency_there_is_no_version_token()
    {
        var result = Generate();
        Assert.DoesNotContain("IVersioned", File(result, "Product.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Version", File(result, "ProductRequest.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("DbUpdateConcurrencyException", File(result, "UpdateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Events_raise_domain_events_and_emit_records_plus_a_handler_stub()
    {
        var result = Generate(useEvents: true);

        // Typed event records + a sample handler (auto-registered by AddRaskCqrs).
        var events = File(result, "ProductEvents.cs");
        Assert.Contains("public sealed record ProductCreated(Guid Id) : INotification;", events, StringComparison.Ordinal);
        Assert.Contains("public sealed record ProductUpdated(Guid Id) : INotification;", events, StringComparison.Ordinal);
        Assert.Contains("public sealed record ProductDeleted(Guid Id) : INotification;", events, StringComparison.Ordinal);
        Assert.Contains("INotificationHandler<ProductCreated>", File(result, "ProductCreatedHandler.cs"), StringComparison.Ordinal);

        // The aggregate raises them; the interceptor (Rask.Data) publishes after commit.
        var entity = File(result, "Product.cs");
        Assert.Contains("entity.Raise(new ProductCreated(entity.Id));", entity, StringComparison.Ordinal);
        Assert.Contains("Raise(new ProductUpdated(Id));", entity, StringComparison.Ordinal);
        Assert.Contains("public void RaiseDeleted() => Raise(new ProductDeleted(Id));", entity, StringComparison.Ordinal);
        Assert.Contains("entity.RaiseDeleted();", File(result, "DeleteProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_events_no_event_records_or_raise_calls()
    {
        var result = Generate();
        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) is "ProductEvents.cs" or "ProductCreatedHandler.cs");
        Assert.DoesNotContain("Raise(", File(result, "Product.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Outbox_makes_events_ioutboxevent_wires_the_table_and_adds_the_package()
    {
        var result = Generate(useOutbox: true);

        // --outbox raises events (like --events) but they implement IOutboxEvent for durable delivery.
        Assert.Contains("public sealed record ProductCreated(Guid Id) : IOutboxEvent;", File(result, "ProductEvents.cs"), StringComparison.Ordinal);
        Assert.Contains("entity.Raise(new ProductCreated(entity.Id));", File(result, "Product.cs"), StringComparison.Ordinal);

        // The DbContext maps the outbox table; the package + DI wiring are applied to Program.cs.
        Assert.Contains("modelBuilder.AddRaskOutbox();", File(result, "AppDbContext.cs"), StringComparison.Ordinal);
        Assert.Contains("Rask.Outbox", result.Packages);
        Assert.Contains(result.ProgramRegistrations, r => r.Contains("AddRaskOutbox<AppDbContext>();", StringComparison.Ordinal));
        Assert.Contains(result.ProgramRegistrations, r => r.Contains("DispatchDomainEventsInProcess = false", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_outbox_no_outbox_wiring()
    {
        var result = Generate(useEvents: true);
        Assert.DoesNotContain("IOutboxEvent", File(result, "ProductEvents.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskOutbox", File(result, "AppDbContext.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Outbox", result.Packages);
    }

    [Fact]
    public void Owned_context_registers_UseRaskSqlite_and_adds_the_package()
    {
        var result = Generate();

        // The generated DbContext factory uses UseRaskSqlite (production pragmas), honouring a
        // ConnectionStrings:App override so a deploy volume works — not the pragma-less raw UseSqlite.
        Assert.Contains(result.ProgramRegistrations, r => r.Contains(".UseRaskSqlite(", StringComparison.Ordinal));
        Assert.Contains(result.ProgramRegistrations, r => r.Contains("builder.Configuration.GetConnectionString(\"App\")", StringComparison.Ordinal));
        Assert.DoesNotContain(result.ProgramRegistrations, r => r.Contains(".UseSqlite(\"Data Source", StringComparison.Ordinal));
        Assert.Contains("Rask.SQLite", result.ProgramUsings);
        Assert.Contains("Rask.SQLite.EntityFrameworkCore", result.Packages);
    }

    [Fact]
    public void Explicit_context_does_not_register_a_factory_or_the_sqlite_package()
    {
        // With --context the existing context owns the registration + the Rask.SQLite.EntityFrameworkCore ref.
        var result = Generate(context: "AppDbContext");

        Assert.DoesNotContain(result.ProgramRegistrations, r => r.Contains("AddDbContextFactory", StringComparison.Ordinal));
        Assert.DoesNotContain("Rask.SQLite.EntityFrameworkCore", result.Packages);
    }

    [Fact]
    public void Mutation_pages_handle_errors_gracefully_with_an_inline_alert()
    {
        var create = File(Generate(), "CreateProduct.cs");
        Assert.Contains("private string? _error;", create, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", create, StringComparison.Ordinal);
        Assert.Contains("_error = \"Something went wrong", create, StringComparison.Ordinal);
        // Plain HTML uses a semantic role="alert"; --bs uses BsAlert.
        Assert.Contains("_error is null ? null : Div(Role: \"alert\")[_error]", create, StringComparison.Ordinal);
        Assert.Contains("_error is null ? null : BsAlert.Color(BsColor.Danger)[_error]", File(Generate(useBs: true), "CreateProduct.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Tests_flag_emits_domain_and_persistence_tests_in_a_sibling_test_project()
    {
        var result = Generate(useTests: true);

        // A domain test (pure Create/Update + value-object validation) and a SQLite round-trip test,
        // both under a sibling <Project>.Tests project mirroring the feature folder.
        var domain = result.Files.Single(f => Path.GetFileName(f.Path) == "ProductTests.cs");
        var persistence = result.Files.Single(f => Path.GetFileName(f.Path) == "ProductsPersistenceTests.cs");
        Assert.Equal(Path.GetFullPath("/proj.Tests/Features/Products"), Path.GetFullPath(Path.GetDirectoryName(domain.Path)!));

        Assert.Contains("namespace MyApp.Tests.Features.Products;", domain.Content, StringComparison.Ordinal);
        Assert.Contains("using MyApp.Features.Products;", domain.Content, StringComparison.Ordinal);
        Assert.Contains("var entity = Product.Create(\"Sample\", 10.25m);", domain.Content, StringComparison.Ordinal);
        Assert.Contains("entity.Update(\"Updated\", 20.50m);", domain.Content, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(\"Sample\", entity.Name.Value);", domain.Content, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(10.25m, entity.Price);", domain.Content, StringComparison.Ordinal);
        Assert.Contains("Assert.Empty(ProductName.Validate(\"Sample\"));", domain.Content, StringComparison.Ordinal);

        Assert.Contains("public sealed class ProductsPersistenceTests : IDisposable", persistence.Content, StringComparison.Ordinal);
        Assert.Contains(".UseSqlite($\"Data Source={_dbPath}\")", persistence.Content, StringComparison.Ordinal);
        Assert.Contains("var entity = await db.Products.SingleAsync();", persistence.Content, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(\"Sample\", entity.Name.Value);", persistence.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Tests_flag_skips_persistence_test_when_reusing_an_existing_context()
    {
        var result = Generate(useTests: true, context: "AppDbContext");

        Assert.Contains(result.Files, f => Path.GetFileName(f.Path) == "ProductTests.cs");
        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path) == "ProductsPersistenceTests.cs");
    }

    [Fact]
    public void Tests_flag_scaffolds_a_test_project_csproj_and_global_usings()
    {
        var result = Generate(useTests: true);

        Assert.Contains(result.CreateIfAbsent, f => Path.GetFileName(f.Path) == "proj.Tests.csproj" && f.Content.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal));
        Assert.Contains(result.CreateIfAbsent, f => Path.GetFileName(f.Path) == "GlobalUsings.cs" && f.Content.Contains("global using Xunit;", StringComparison.Ordinal));
        Assert.NotNull(result.TestProject);
        Assert.Contains("xunit", result.TestProject!.Packages);
        Assert.Contains("Microsoft.NET.Test.Sdk", result.TestProject.Packages);
    }

    [Fact]
    public void Without_tests_no_test_project_is_scaffolded()
    {
        var result = Generate();

        Assert.Empty(result.CreateIfAbsent);
        Assert.Null(result.TestProject);
    }

    [Fact]
    public void Without_tests_flag_no_test_files_are_generated()
    {
        Assert.DoesNotContain(Generate().Files, f => Path.GetFileName(f.Path).EndsWith("Tests.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Plural_override_drives_names_and_route()
    {
        var result = FeatureGenerator.Generate(
            new ProjectContext("/proj", "MyApp"),
            "/proj",
            new FeatureSpec(new EntitySpec("Person", "People", Fields), []),
            new FeatureOptions { IdType = "Guid", Validation = "valueobjects" });

        Assert.Contains(result.Files, f => f.Path.EndsWith("PeoplePage.cs", StringComparison.Ordinal));
        Assert.Contains("[Route(\"/people\")]", File(result, "PeoplePage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_bs_pages_are_plain_unstyled_html()
    {
        var result = Generate(); // core (no --bs)

        foreach (var file in new[] { "ProductsPage.cs", "CreateProduct.cs", "UpdateProduct.cs", "DeleteProduct.cs" })
        {
            Assert.DoesNotContain("Class:", File(result, file), StringComparison.Ordinal); // no styling / classes at all
        }

        // A field is just a label + bound Input, no framework classes.
        Assert.Contains("Input.Bind(() => _form.Price).Id(\"price\")", File(result, "CreateProduct.cs"), StringComparison.Ordinal);
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
        var result = Generate();
        var notes = result.Notes!;

        Assert.Contains(result.ProgramRegistrations, r => r.Contains("AddRaskCqrs();", StringComparison.Ordinal));
        Assert.Contains(result.ProgramRegistrations, r => r.Contains("AddDbContextFactory<AppDbContext>", StringComparison.Ordinal));
        Assert.Contains("rask db add AddProduct", notes, StringComparison.Ordinal);
        Assert.Contains("rask db update", notes, StringComparison.Ordinal);
        // The packages are added to the project automatically (not just printed). SQLitePCLRaw is a security
        // reference, not a convenience one: EF Core Sqlite pins the 2.1.11 family, which carries CVE-2025-6965,
        // and only a direct reference lifts it. Don't drop it from this list without reading
        // Directory.Packages.props.
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3", "Microsoft.EntityFrameworkCore.Design", "Rask.Cqrs", "Rask.Data", "Rask.SQLite.EntityFrameworkCore"],
            result.Packages);
    }

    [Fact]
    public void Feature_packages_include_bootstrap_and_the_validation_library()
    {
        Assert.Contains("Rask.Bootstrap", Generate(useBs: true).Packages);
        Assert.Contains("Rask.Validation.DataAnnotations", Generate(validation: "dataannotations").Packages);
        Assert.Contains("Rask.Validation.FluentValidation", Generate(validation: "fluent").Packages);
    }

    [Fact]
    public void Explicit_context_skips_the_dbcontext_file_and_wires_handlers_to_it()
    {
        var result = Generate(context: "AppDbContext");

        Assert.DoesNotContain(result.Files, f => f.Path.EndsWith("DbContext.cs", StringComparison.Ordinal));
        Assert.Contains("IDbContextFactory<AppDbContext>", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
        // The DbSet the user's context needs is surfaced for the command to insert (not baked into a new file).
        Assert.Contains(result.ContextDbSets, s => s.Contains("public DbSet<Product> Products => Set<Product>();", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_context_with_a_resolved_namespace_emits_the_cross_namespace_using()
    {
        // When the command resolves the --context class to another namespace, the slice imports it so it compiles.
        var result = Generate(context: "AppDbContext", contextNamespace: "MyApp.Data");

        Assert.Contains("using MyApp.Data;", File(result, "ProductsPage.cs"), StringComparison.Ordinal);
        // The DbSet references Product (in the feature namespace), so the context needs that using added too.
        Assert.Contains("MyApp.Features.Products", result.ContextUsings);
    }
}

/// <summary>
/// A run that names relationship targets generates <b>every</b> entity in it, each as an independent root
/// with its own folder, namespace and full CRUD, all sharing one DbContext — and emits the relationship
/// itself: the foreign key on the dependent, navigation properties both ways, and the EF mapping.
/// </summary>
public sealed class FeatureGeneratorMultiEntityTests
{
    private static readonly EntitySpec Post = new("Post", "Posts", [new FieldSpec("Title", "string", IsNullable: false, MaxLength: 200)]);
    private static readonly EntitySpec Comment = new("Comment", "Comments", [new FieldSpec("Body", "string", IsNullable: false, MaxLength: 200)]);
    private static readonly EntitySpec Tag = new("Tag", "Tags", [new FieldSpec("Name", "string", IsNullable: false, MaxLength: 200)]);

    private static ScaffoldResult GenerateRelationship(Cardinality card, bool optional = false) =>
        FeatureGenerator.Generate(
            new ProjectContext("/proj", "MyApp"),
            "/proj",
            new FeatureSpec(Post, [new RelationshipSpec(card, optional, Post, card is Cardinality.ManyToMany ? Tag : Comment)]),
            new FeatureOptions { IdType = "Guid", Validation = "dataannotations" });

    [Fact]
    public void One_to_many_puts_the_fk_and_reference_nav_on_the_dependent_and_a_collection_on_the_principal()
    {
        var result = GenerateRelationship(Cardinality.OneToMany);

        var comment = File(result, "Comment.cs");
        Assert.Contains("public Guid PostId { get; private set; }", comment, StringComparison.Ordinal);
        Assert.Contains("public Post? Post { get; private set; }", comment, StringComparison.Ordinal);
        Assert.Contains("using MyApp.Features.Posts;", comment, StringComparison.Ordinal);

        Assert.Contains("public ICollection<Comment> Comments { get; } = new List<Comment>();", File(result, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("entity.HasOne(x => x.Post).WithMany(p => p.Comments).HasForeignKey(x => x.PostId);", File(result, "CommentConfiguration.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Many_to_many_maps_from_two_collection_navs_with_no_foreign_key()
    {
        var result = GenerateRelationship(Cardinality.ManyToMany);

        Assert.Contains("public ICollection<Tag> Tags { get; } = new List<Tag>();", File(result, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("public ICollection<Post> Posts { get; } = new List<Post>();", File(result, "Tag.cs"), StringComparison.Ordinal);
        Assert.Contains("entity.HasMany(x => x.Tags).WithMany(y => y.Posts);", File(result, "PostConfiguration.cs"), StringComparison.Ordinal);
        // No foreign-key column — the join table is implicit.
        Assert.DoesNotContain("TagId", File(result, "Post.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void One_to_one_uses_a_reference_nav_both_ways()
    {
        var result = GenerateRelationship(Cardinality.OneToOne);

        Assert.Contains("public Post? Post { get; private set; }", File(result, "Comment.cs"), StringComparison.Ordinal);
        Assert.Contains("public Comment? Comment { get; private set; }", File(result, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("entity.HasOne(x => x.Post).WithOne(p => p.Comment).HasForeignKey<Comment>(x => x.PostId);", File(result, "CommentConfiguration.cs"), StringComparison.Ordinal);
    }

    private static ScaffoldResult Generate(string? context = null, string? output = null) =>
        FeatureGenerator.Generate(
            new ProjectContext("/proj", "MyApp"),
            "/proj",
            new FeatureSpec(Post, [new RelationshipSpec(Cardinality.OneToMany, IsOptional: false, Post, Comment)]),
            new FeatureOptions
            {
                IdType = "Guid",
                Validation = "valueobjects",
                ExistingContext = context,
                OutputOverride = output,
            });

    private static string File(ScaffoldResult result, string fileName) =>
        result.Files.Single(f => Path.GetFileName(f.Path) == fileName).Content;

    private static string Directory(ScaffoldResult result, string fileName) =>
        Path.GetFullPath(Path.GetDirectoryName(result.Files.Single(f => Path.GetFileName(f.Path) == fileName).Path)!);

    [Fact]
    public void Every_entity_gets_its_own_full_crud_slice()
    {
        var names = Generate().Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            [
                "AppDbContext.cs",
                "Comment.cs", "CommentBody.cs", "CommentConfiguration.cs", "CommentRequest.cs", "CommentsPage.cs",
                "CreateComment.cs", "CreatePost.cs", "DeleteComment.cs", "DeletePost.cs",
                "Post.cs", "PostConfiguration.cs", "PostRequest.cs", "PostTitle.cs",
                "PostsPage.cs", "UpdateComment.cs", "UpdatePost.cs",
            ],
            names);
    }

    [Fact]
    public void Each_entity_lands_in_its_own_feature_folder()
    {
        var result = Generate();

        Assert.Equal(Path.GetFullPath("/proj/Features/Posts"), Directory(result, "Post.cs"));
        Assert.Equal(Path.GetFullPath("/proj/Features/Comments"), Directory(result, "Comment.cs"));
    }

    [Fact]
    public void Each_entity_gets_the_namespace_of_its_own_folder()
    {
        var result = Generate();

        Assert.Contains("namespace MyApp.Features.Posts;", File(result, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("namespace MyApp.Features.Comments;", File(result, "Comment.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_gets_its_own_route_not_one_under_the_root()
    {
        Assert.Contains("[Route(\"/comments\")]", File(Generate(), "CommentsPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void One_dbcontext_holds_a_dbset_for_every_entity()
    {
        var context = File(Generate(), "AppDbContext.cs");

        Assert.Contains("public DbSet<Post> Posts => Set<Post>();", context, StringComparison.Ordinal);
        Assert.Contains("public DbSet<Comment> Comments => Set<Comment>();", context, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dbcontext_lives_in_features_shared_and_is_generated_once()
    {
        var result = Generate();

        Assert.Single(result.Files, f => Path.GetFileName(f.Path).EndsWith("DbContext.cs", StringComparison.Ordinal));
        Assert.Equal(Path.GetFullPath("/proj/Features/Shared"), Directory(result, "AppDbContext.cs"));
    }

    [Fact]
    public void The_dbcontext_usings_reach_every_entitys_namespace()
    {
        var context = File(Generate(), "AppDbContext.cs");

        // It sits beside neither entity, so it imports both.
        Assert.Contains("using MyApp.Features.Comments;", context, StringComparison.Ordinal);
        Assert.Contains("using MyApp.Features.Posts;", context, StringComparison.Ordinal);
    }

    [Fact]
    public void A_targets_handlers_can_see_the_shared_dbcontext()
    {
        var deleteComment = File(Generate(), "DeleteComment.cs");

        // The context lives in Features/Shared, so every slice needs a using to name it.
        Assert.Contains("using MyApp.Features.Shared;", deleteComment, StringComparison.Ordinal);
        Assert.Contains("IDbContextFactory<AppDbContext>", deleteComment, StringComparison.Ordinal);
    }

    [Fact]
    public void The_roots_slice_imports_the_shared_context_too()
    {
        var deletePost = File(Generate(), "DeletePost.cs");

        // Not its own namespace — the root's slice never imports Features.Posts …
        Assert.DoesNotContain("using MyApp.Features.Posts;", deletePost, StringComparison.Ordinal);
        Assert.Contains("using MyApp.Features.Shared;", deletePost, StringComparison.Ordinal);
        Assert.Contains("IDbContextFactory<AppDbContext>", deletePost, StringComparison.Ordinal);
    }

    [Fact]
    public void With_an_external_context_no_dbcontext_is_generated_and_the_handlers_name_it()
    {
        var result = Generate(context: "AppDbContext");

        Assert.DoesNotContain(result.Files, f => Path.GetFileName(f.Path).EndsWith("DbContext.cs", StringComparison.Ordinal));
        Assert.Contains("IDbContextFactory<AppDbContext>", File(result, "DeleteComment.cs"), StringComparison.Ordinal);
        // (The Comment slice does import MyApp.Features.Posts now — but for the Post navigation, not the
        // context, whose location stays unassumed until the command resolves it.)
    }

    [Fact]
    public void With_an_output_override_every_entity_shares_one_folder_so_no_cross_usings_are_emitted()
    {
        var result = Generate(output: "Slice");

        Assert.All(result.Files, f => Assert.Equal(Path.GetFullPath("/proj/Slice"), Path.GetFullPath(Path.GetDirectoryName(f.Path)!)));
        Assert.DoesNotContain("using MyApp.Slice;", File(result, "DeleteComment.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("using MyApp.Slice;", File(result, "AppDbContext.cs"), StringComparison.Ordinal);
    }
}
