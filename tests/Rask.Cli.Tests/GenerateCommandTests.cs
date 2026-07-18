using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class GenerateCommandTests
{
    private const string ProjectDir = "/proj";

    [Fact]
    public async Task Generates_a_page_into_the_project()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Products"], CancellationToken.None);

        Assert.Equal(0, exit);
        var path = Path.GetFullPath("/proj/Features/Products/ProductsPage.cs");
        Assert.True(fs.Files.ContainsKey(path));
        Assert.Contains("namespace MyApp.Features.Products;", fs.Files[path], StringComparison.Ordinal);
        // Written files are reported with the shared "  + <path>" marker (unified with `rask new`).
        Assert.Contains("+ Features/Products/ProductsPage.cs", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generates_a_component_into_the_project()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.Files.ContainsKey(Path.GetFullPath("/proj/Components/PriceTag.cs")));
    }

    [Fact]
    public async Task Generates_a_job_with_its_handler_and_adds_its_packages()
    {
        var (console, fs, process, command) = BuildWithProcess();

        var exit = await command.ExecuteAsync(["job", "SendWelcomeEmail"], CancellationToken.None);

        Assert.Equal(0, exit);
        var path = Path.GetFullPath("/proj/Jobs/SendWelcomeEmail.cs");
        Assert.True(fs.Files.ContainsKey(path));
        Assert.Contains("namespace MyApp.Jobs;", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains("record SendWelcomeEmail : IJob", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains("ICommandHandler<SendWelcomeEmail>", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains(process.Invocations, i => i.Arguments is ["add", "package", "Rask.Jobs"]);
        Assert.Contains(process.Invocations, i => i.Arguments is ["add", "package", "Rask.Cqrs"]);
        Assert.Contains("AddRaskJobs", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_alias_j_scaffolds_a_job()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["j", "Cleanup"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.Files.ContainsKey(Path.GetFullPath("/proj/Jobs/Cleanup.cs")));
    }

    [Fact]
    public async Task Generates_an_email_component_and_adds_its_package()
    {
        var (console, fs, process, command) = BuildWithProcess();

        var exit = await command.ExecuteAsync(["email", "WelcomeEmail"], CancellationToken.None);

        Assert.Equal(0, exit);
        var path = Path.GetFullPath("/proj/Emails/WelcomeEmail.cs");
        Assert.True(fs.Files.ContainsKey(path));
        Assert.Contains("namespace MyApp.Emails;", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains("class WelcomeEmail : Component", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains("Body(new WelcomeEmail())", fs.Files[path], StringComparison.Ordinal);
        Assert.Contains(process.Invocations, i => i.Arguments is ["add", "package", "Rask.Mail"]);
        Assert.Contains("AddRaskMail", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_alias_e_scaffolds_an_email()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["e", "Receipt"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.Files.ContainsKey(Path.GetFullPath("/proj/Emails/Receipt.cs")));
    }

    [Fact]
    public async Task Route_on_job_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["job", "Cleanup", "--route", "/x"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--route only applies to 'generate page'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dry_run_writes_nothing_but_prints_the_content()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Products", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("ProductsPage.cs", StringComparison.Ordinal));
        Assert.Contains("[dry-run]", console.OutText, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProductsPage", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_overwrite_without_force()
    {
        var (console, fs, command) = Build();
        fs.Seed("/proj/Components/PriceTag.cs", "// existing");

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("// existing", fs.Files[Path.GetFullPath("/proj/Components/PriceTag.cs")]);
        Assert.Contains("Refusing to overwrite", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Force_overwrites_an_existing_file()
    {
        var (_, fs, command) = Build();
        fs.Seed("/proj/Components/PriceTag.cs", "// existing");

        var exit = await command.ExecuteAsync(["component", "PriceTag", "--force"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("class PriceTag", fs.Files[Path.GetFullPath("/proj/Components/PriceTag.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_type_name_fails()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "2Cool"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Value.Contains("class", StringComparison.Ordinal));
        Assert.Contains("not a valid C# type name", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generates_a_full_feature_slice()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string,Price:decimal"], CancellationToken.None);

        Assert.Equal(0, exit);
        foreach (var file in new[] { "Product.cs", "ProductRequest.cs", "ProductsDbContext.cs", "ProductsPage.cs", "CreateProduct.cs", "UpdateProduct.cs", "DeleteProduct.cs" })
        {
            Assert.Contains(fs.Files, f => f.Key.EndsWith(file, StringComparison.Ordinal));
        }

        Assert.Contains("Next steps:", console.OutText, StringComparison.Ordinal);
    }

    private const string ProgramCs =
        "using MyApp;\n" +
        "var builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddRask();\n" +
        "var app = builder.Build();\n" +
        "app.Run();\n";

    [Fact]
    public async Task Feature_wires_the_service_registrations_into_Program_cs()
    {
        var (console, fs, command) = Build();
        fs.Seed("/proj/Program.cs", ProgramCs);

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string,Price:decimal"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.Files[Path.GetFullPath("/proj/Program.cs")];
        // Framework services + the run's DbContext factory are inserted, not just printed.
        Assert.Contains("builder.Services.AddRaskCqrs();", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddRaskData();", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddDbContextFactory<ProductsDbContext>((sp, o) => o", program, StringComparison.Ordinal);
        Assert.Contains(".AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));", program, StringComparison.Ordinal);
        // The usings the registrations need are added too.
        Assert.Contains("using Rask.Cqrs;", program, StringComparison.Ordinal);
        Assert.Contains("using Rask.Data;", program, StringComparison.Ordinal);
        Assert.Contains("using MyApp.Features.Products;", program, StringComparison.Ordinal);
        Assert.Contains("using Microsoft.EntityFrameworkCore.Diagnostics;", program, StringComparison.Ordinal);
        Assert.Contains("Registered", console.OutText, StringComparison.Ordinal);
        Assert.Contains("Program.cs", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Program_wiring_is_idempotent()
    {
        var (_, fs, command) = Build();
        // A Program.cs already carrying the registrations (e.g. a re-run) must not gain duplicates.
        fs.Seed("/proj/Program.cs", ProgramCs +
            "builder.Services.AddRaskCqrs();\n" +
            "builder.Services.AddRaskData();\n" +
            "builder.Services.AddDbContextFactory<ProductsDbContext>((sp, o) => o\n" +
            "    .UseSqlite(\"Data Source=app.db\")\n" +
            "    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));\n");

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--force"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.Files[Path.GetFullPath("/proj/Program.cs")];
        Assert.Equal(1, Occurrences(program, "builder.Services.AddRaskCqrs();"));
        Assert.Equal(1, Occurrences(program, "builder.Services.AddDbContextFactory<ProductsDbContext>"));
    }

    [Fact]
    public async Task Feature_prints_a_manual_fallback_when_there_is_no_Program_cs()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.False(fs.Files.ContainsKey(Path.GetFullPath("/proj/Program.cs")));
        Assert.Contains("Couldn't find Program.cs", console.OutText, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddRaskCqrs();", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_context_wires_framework_services_but_not_a_dbcontext()
    {
        var (console, fs, command) = Build();
        fs.Seed("/proj/Program.cs", ProgramCs);

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--context", "AppDbContext"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.Files[Path.GetFullPath("/proj/Program.cs")];
        Assert.Contains("builder.Services.AddRaskCqrs();", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddRaskData();", program, StringComparison.Ordinal);
        // The context is the user's own — we don't register a factory for it.
        Assert.DoesNotContain("AddDbContextFactory", program, StringComparison.Ordinal);
        // …but the next-steps still remind them to surface the entity on their context.
        Assert.Contains("public DbSet<Product> Products => Set<Product>();", console.OutText, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task Feature_adds_the_required_nuget_packages_automatically()
    {
        var (_, _, process, command) = BuildWithProcess();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string,Price:decimal", "--bs"], CancellationToken.None);

        Assert.Equal(0, exit);
        var adds = process.Invocations
            .Where(i => i.Arguments is ["add", "package", _])
            .Select(i => i.Arguments[2])
            .ToArray();
        // SQLitePCLRaw is really added to the project, not merely printed — it's the direct reference that
        // lifts EF Core Sqlite's vulnerable 2.1.11 pin (CVE-2025-6965), and nothing else does.
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3", "Microsoft.EntityFrameworkCore.Design", "Rask.Cqrs", "Rask.Data", "Rask.Bootstrap"],
            adds);
    }

    [Fact]
    public async Task Feature_no_restore_skips_package_adds()
    {
        var (console, _, process, command) = BuildWithProcess();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(process.Invocations, i => i.Arguments.Count > 0 && i.Arguments[0] == "add");
        Assert.Contains("Skipped adding packages", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feature_dry_run_adds_no_packages()
    {
        var (_, _, process, command) = BuildWithProcess();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(process.Invocations);
    }

    [Fact]
    public async Task Page_generation_adds_no_packages()
    {
        var (_, _, process, command) = BuildWithProcess();

        await command.ExecuteAsync(["page", "Products"], CancellationToken.None);

        Assert.Empty(process.Invocations);
    }

    [Fact]
    public async Task Feature_without_fields_fails()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(fs.Files.Where(f => f.Key.EndsWith("Product.cs", StringComparison.Ordinal)).ToArray());
        Assert.Contains("needs fields", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feature_takes_fields_positionally()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "Name:string", "Price:decimal"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        // Same output as the --fields form: entity + create command scaffolded with both fields.
        Assert.Contains(fs.Files, f => f.Key.EndsWith("CreateProduct.cs", StringComparison.Ordinal));
        var entity = fs.Files.Single(f => f.Key.EndsWith($"{Path.DirectorySeparatorChar}Product.cs", StringComparison.Ordinal)).Value;
        Assert.Contains("Name", entity, StringComparison.Ordinal);
        Assert.Contains("Price", entity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Positional_fields_and_fields_option_together_are_rejected()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "Name:string", "--fields", "Price:decimal"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("CreateProduct.cs", StringComparison.Ordinal));
        Assert.Contains("not both", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extra_positional_on_a_page_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Home", "Name:string"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Unexpected argument 'Name:string'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feature_with_unknown_field_type_fails()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:blob"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown field type 'blob'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_context_omits_the_dbcontext_file()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--context", "AppDbContext"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("DbContext.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Field_named_like_the_entity_is_rejected()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Product:string"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("Product.cs", StringComparison.Ordinal));
        Assert.Contains("can't share the entity's name", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_context_name_is_rejected()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--context", "App-Db Context"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(fs.Files.Where(f => f.Key.EndsWith("Product.cs", StringComparison.Ordinal)).ToArray());
        Assert.Contains("not a valid C# type name for --context", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plural_override_is_honored()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Person", "--fields", "Name:string", "--plural", "People"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(fs.Files, f => f.Key.EndsWith("PeoplePage.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fields_on_a_page_are_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Products", "--fields", "Name:string"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--fields only applies to 'generate feature'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_feature_option_only_names_itself_not_every_feature_option()
    {
        var (console, _, command) = Build();

        await command.ExecuteAsync(["page", "Products", "--fields", "Name:string"], CancellationToken.None);

        // The message is built from what was actually passed, so it can't list flags the user never typed.
        Assert.DoesNotContain("--outbox", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_restore_on_a_page_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Dashboard", "--no-restore"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--no-restore only applies to 'generate feature'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relationship_is_refused_rather_than_silently_dropped()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Post", "Title:string", "1:n", "Comment", "Body:text"], CancellationToken.None);

        // The grammar lands ahead of the emitter — until it arrives, generating Post and discarding Comment
        // would quietly lose what was asked for. Delete this test when relationship emission ships.
        Assert.Equal(1, exit);
        Assert.Contains("Relationships aren't generated yet", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith(".cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_relationship_is_validated_before_it_is_refused()
    {
        var (console, _, command) = Build();

        await command.ExecuteAsync(["feature", "Post", "Title:string", "1:m", "Comment", "Body:text"], CancellationToken.None);

        Assert.Contains("Unknown cardinality '1:m'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kind_aliases_f_and_c_resolve()
    {
        var (_, fs, command) = Build();

        Assert.Equal(0, await command.ExecuteAsync(["f", "Product", "--fields", "Name:string"], CancellationToken.None));
        Assert.Contains(fs.Files, file => file.Key.EndsWith("CreateProduct.cs", StringComparison.Ordinal));

        Assert.Equal(0, await command.ExecuteAsync(["c", "Widget"], CancellationToken.None));
        Assert.Contains(fs.Files, file => file.Key.EndsWith("Widget.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Id_type_int_is_honored()
    {
        var (_, fs, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--id", "int"], CancellationToken.None);

        Assert.Equal(0, exit);
        var entity = fs.Files.Single(f => Path.GetFileName(f.Key) == "Product.cs").Value;
        Assert.Contains("public sealed class Product : Entity<int>", entity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_id_type_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["feature", "Product", "--fields", "Name:string", "--id", "ulid"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--id must be", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_artifact_fails()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["controller", "Products"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown artifact 'controller'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_name_fails()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("A name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_route_is_rejected_before_writing()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Reports", "--route", "a\"b"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Key.EndsWith("ReportsPage.cs", StringComparison.Ordinal));
        Assert.Contains("not a valid route path", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reserved_keyword_name_is_rejected()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(["component", "class"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fs.Files, f => f.Value.Contains("class class", StringComparison.Ordinal));
        Assert.Contains("not a valid C# type name", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_on_component_is_rejected()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["component", "PriceTag", "--route", "/x"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--route only applies", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_cleanly_outside_a_project()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem(); // no csproj seeded
        var command = new GenerateCommand(console, fs, new FakeProcessRunner(), ProjectDir);

        var exit = await command.ExecuteAsync(["component", "PriceTag"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Couldn't find a single .csproj", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feature_inherits_defaults_from_dot_rask_generate_json()
    {
        var (console, fs, command) = Build();
        // A team default of --bs, with no --bs on the command line.
        fs.Seed("/proj/.rask/generate.json", "{ \"bs\": true }");

        var exit = await command.ExecuteAsync(["feature", "Product", "Name:string", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        // The Bootstrap create page renders a BsAlert — proof the config default was applied.
        Assert.Contains("BsAlert", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_defaults_writes_the_run_flags_to_the_config_file()
    {
        var (console, fs, command) = Build();

        var exit = await command.ExecuteAsync(
            ["feature", "Product", "Name:string", "--bs", "--tests", "--no-restore", "--save-defaults"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        var configPath = Path.GetFullPath("/proj/.rask/generate.json");
        Assert.True(fs.Files.ContainsKey(configPath));
        Assert.Contains("\"bs\": true", fs.Files[configPath], StringComparison.Ordinal);
        Assert.Contains("\"tests\": true", fs.Files[configPath], StringComparison.Ordinal);
        Assert.Contains("Saved generate defaults", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_defaults_only_applies_to_feature()
    {
        var (console, _, command) = Build();

        var exit = await command.ExecuteAsync(["page", "Dashboard", "--save-defaults"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--save-defaults only applies to 'generate feature'", console.ErrorText, StringComparison.Ordinal);
    }

    private static (StringConsole Console, FakeFileSystem Fs, GenerateCommand Command) Build()
    {
        var (console, fs, _, command) = BuildWithProcess();
        return (console, fs, command);
    }

    private static (StringConsole Console, FakeFileSystem Fs, FakeProcessRunner Process, GenerateCommand Command) BuildWithProcess()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var process = new FakeProcessRunner();
        fs.Seed("/proj/MyApp.csproj", "<Project></Project>");
        return (console, fs, process, new GenerateCommand(console, fs, process, ProjectDir));
    }
}
