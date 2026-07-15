using System.Globalization;
using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a full CQRS + EF Core CRUD vertical slice under <c>Features/&lt;Plural&gt;/</c>. Each slice
/// is one file: an encapsulated entity (<c>Create</c>/<c>Update</c>, Guid id by default), a shared
/// request the forms bind to, a DbContext (unless <c>--context</c>), and <c>&lt;Plural&gt;Page</c> /
/// <c>Create&lt;Entity&gt;</c> / <c>Update&lt;Entity&gt;</c> / <c>Delete&lt;Entity&gt;</c> components that
/// each carry the CQRS message + handler they dispatch through <c>IDispatcher</c>. The output compiles
/// as-is; a printed next-steps note covers the DI registration and the migration.
/// </summary>
internal static class FeatureGenerator
{
    public static ScaffoldResult Generate(
        ProjectContext project,
        string baseDirectory,
        string entityName,
        IReadOnlyList<FieldSpec> fields,
        string idType,
        string validation,
        bool useBs,
        bool useModal,
        bool useTests,
        string? contextOverride,
        string? pluralOverride,
        string? outputOverride)
    {
        var plural = pluralOverride ?? Pluralizer.Pluralize(entityName);
        var route = Identifiers.ToRoutePath(plural);
        var idConstraint = idType == "Guid" ? "guid" : idType;
        var generateContext = contextOverride is null;
        var context = contextOverride ?? plural + "DbContext";
        var useValueObjects = validation == "valueobjects";

        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Features", plural);
        var ns = project.NamespaceFor(targetDirectory);

        var tokens = new (string, string)[]
        {
            ("__NS__", ns), ("__ENTITY__", entityName), ("__PLURAL__", plural), ("__CONTEXT__", context),
            ("__ROUTE__", route), ("__IDTYPE__", idType), ("__IDCONSTRAINT__", idConstraint),
            ("__CREATEARGS__", RequestArgs(fields, "command.Request")),
            ("__HEADERS__", TableHeaders(fields)), ("__CELLS__", TableCells(fields, useValueObjects)),
            ("__FORMFIELDS__", FormFields(entityName, fields, useValueObjects, useBs)), ("__COPYTOFORM__", CopyToForm(fields, useValueObjects)),
            ("__CONFIGPROPS__", ConfigProperties(entityName, fields, useValueObjects)),
            ("__VALIDATOR__", FormValidator(entityName, validation)),
        };

        var listTemplate = useModal ? BsModalListTemplate : useBs ? BsListPageTemplate : ListPageTemplate;
        var files = new List<ScaffoldFile>
        {
            new(Path.Combine(targetDirectory, entityName + ".cs"), RenderEntity(ns, entityName, fields, idType, useValueObjects)),
            new(Path.Combine(targetDirectory, entityName + "Request.cs"), RenderRequest(ns, entityName, fields, validation)),
            new(Path.Combine(targetDirectory, entityName + "Configuration.cs"), Apply(ConfigurationTemplate, tokens)),
            new(Path.Combine(targetDirectory, plural + "Page.cs"), Apply(listTemplate, tokens)),
            new(Path.Combine(targetDirectory, "Delete" + entityName + ".cs"), Apply(useBs ? BsDeleteTemplate : DeleteTemplate, tokens)),
        };

        // --modal puts create + update in a BsModal on the list page; otherwise they are separate pages.
        if (!useModal)
        {
            files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "Create" + entityName + ".cs"), Apply(useBs ? BsCreateTemplate : CreateTemplate, tokens)));
            files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "Update" + entityName + ".cs"), Apply(useBs ? BsUpdateTemplate : UpdateTemplate, tokens)));
        }

        // valueobjects mode: one value object per required-string field, each owning its validation.
        foreach (var field in fields.Where(f => IsValueObject(f, useValueObjects)))
        {
            files.Add(new ScaffoldFile(
                Path.Combine(targetDirectory, ValueObjectName(entityName, field) + ".cs"),
                RenderValueObject(ns, entityName, field)));
        }

        // fluent mode: a FluentValidation validator for the request.
        if (validation == "fluent")
        {
            files.Add(new ScaffoldFile(
                Path.Combine(targetDirectory, entityName + "RequestValidator.cs"),
                Apply(FluentValidatorTemplate, tokens).Replace("__RULES__", FluentRules(fields), StringComparison.Ordinal)));
        }

        if (generateContext)
        {
            files.Insert(2, new ScaffoldFile(Path.Combine(targetDirectory, context + ".cs"), Apply(DbContextTemplate, tokens)));
        }

        // --tests: a sibling <Project>.Tests project gets a domain test (Create/Update + value-object
        // validation) and, when we own the DbContext, a SQLite round-trip persistence test.
        if (useTests)
        {
            var trimmed = project.ProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var testProjectDir = Path.Combine(Path.GetDirectoryName(trimmed) ?? trimmed, Path.GetFileName(trimmed) + ".Tests");
            var testDirectory = Path.Combine(testProjectDir, "Features", plural);
            var testNamespace = project.RootNamespace + ".Tests.Features." + plural;

            files.Add(new ScaffoldFile(
                Path.Combine(testDirectory, entityName + "Tests.cs"),
                RenderDomainTests(testNamespace, ns, entityName, fields, useValueObjects)));

            if (generateContext)
            {
                files.Add(new ScaffoldFile(
                    Path.Combine(testDirectory, plural + "PersistenceTests.cs"),
                    RenderPersistenceTests(testNamespace, ns, entityName, plural, context, idType, fields, useValueObjects)));
            }
        }

        return new ScaffoldResult(files, RenderNextSteps(context, entityName, plural, route, generateContext, validation, useBs, useTests))
        {
            Packages = FeaturePackages(validation, useBs),
        };
    }

    // The form-level validator component wired at the top of the create/edit forms (empty for the
    // value-objects default, which validates per-input instead).
    private static string FormValidator(string entity, string validation) => validation switch
    {
        "dataannotations" => "                    DataAnnotationsValidator(),\n",
        "fluent" => $"                    FluentValidationValidator(new {entity}RequestValidator()),\n",
        _ => "",
    };

    // FluentValidation RuleFor lines for the string fields (NotEmpty for required, MaximumLength for all).
    private static string FluentRules(IReadOnlyList<FieldSpec> fields) =>
        string.Join("\n", fields.Where(f => f.IsString).Select(f =>
        {
            var notEmpty = f.IsNullable ? "" : ".NotEmpty()";
            return $"        RuleFor(x => x.{f.Name}){notEmpty}.MaximumLength({f.MaxLength!.Value.ToString(CultureInfo.InvariantCulture)});";
        }));

    // ---- entity + shared request (StringBuilder — per-field attributes make raw strings awkward) ----

    // A required (non-nullable) string field is modelled as a value object that owns its own validation
    // (built-in, dependency-free) — only in the default 'valueobjects' mode. The --validation
    // dataannotations/fluent modes keep everything primitive (POCO) and validate on the request instead.
    private static bool IsValueObject(FieldSpec f, bool useValueObjects) => useValueObjects && f.IsString && !f.IsNullable;

    private static string ValueObjectName(string entity, FieldSpec f) => entity + f.Name;

    private static string EntityPropertyType(string entity, FieldSpec f, bool useValueObjects) =>
        IsValueObject(f, useValueObjects) ? ValueObjectName(entity, f) : f.PropertyType;

    internal static string RenderEntity(string ns, string entity, IReadOnlyList<FieldSpec> fields, string idType, bool useValueObjects)
    {
        var sb = new StringBuilder();
        sb.Append("namespace ").Append(ns).Append(";\n\n");
        sb.Append("public sealed class ").Append(entity).Append("\n{\n");
        sb.Append("    private ").Append(entity).Append("() { } // EF Core materialization\n\n");

        // The ctor takes the value-object types; Create/Update take primitives and wrap them via Create.
        var ctorParams = string.Join(", ", fields.Select(f => $"{EntityPropertyType(entity, f, useValueObjects)} {Identifiers.ToCamelCase(f.Name)}"));
        sb.Append("    private ").Append(entity).Append('(').Append(ctorParams).Append(")\n    {\n");
        sb.Append(Assignments(fields, "        ")).Append("\n    }\n\n");
        sb.Append("    public ").Append(idType).Append(" Id { get; private set; }");
        sb.Append(idType == "Guid" ? " = Guid.NewGuid();\n" : "\n");

        foreach (var field in fields)
        {
            sb.Append("\n    public ").Append(EntityPropertyType(entity, field, useValueObjects)).Append(' ').Append(field.Name).Append(" { get; private set; }");
            if (field.Initializer is not null && !IsValueObject(field, useValueObjects))
            {
                sb.Append(' ').Append(field.Initializer).Append(';');
            }

            sb.Append('\n');
        }

        var createParams = string.Join(", ", fields.Select(f => $"{f.PropertyType} {Identifiers.ToCamelCase(f.Name)}"));
        var createArgs = string.Join(", ", fields.Select(f => WrapPrimitive(entity, f, useValueObjects)));
        sb.Append("\n    public static ").Append(entity).Append(" Create(").Append(createParams).Append(") => new(").Append(createArgs).Append(");\n\n");

        sb.Append("    public void Update(").Append(createParams).Append(")\n    {\n");
        sb.Append(string.Join("\n", fields.Select(f => $"        this.{f.Name} = {WrapPrimitive(entity, f, useValueObjects)};"))).Append("\n    }\n}\n");
        return sb.ToString();
    }

    // Wrap a primitive parameter into its value object where the field has one, else pass it through.
    private static string WrapPrimitive(string entity, FieldSpec f, bool useValueObjects)
    {
        var param = Identifiers.ToCamelCase(f.Name);
        return IsValueObject(f, useValueObjects) ? $"{ValueObjectName(entity, f)}.Create({param})" : param;
    }

    internal static string RenderValueObject(string ns, string entity, FieldSpec f)
    {
        var name = ValueObjectName(entity, f);
        var max = f.MaxLength!.Value.ToString(CultureInfo.InvariantCulture);
        return Apply(ValueObjectTemplate,
        [
            ("__NS__", ns), ("__VO__", name), ("__FIELD__", f.Name), ("__MAX__", max),
        ]);
    }

    private static string RenderRequest(string ns, string entity, IReadOnlyList<FieldSpec> fields, string validation)
    {
        // In --validation dataannotations mode the DataAnnotationsValidator reads attributes off this
        // request, so emit them here. Other modes validate elsewhere (value objects / a fluent validator).
        var annotate = validation == "dataannotations";
        var sb = new StringBuilder();
        if (annotate && fields.Any(f => f.IsString))
        {
            sb.Append("using System.ComponentModel.DataAnnotations;\n\n");
        }

        sb.Append("namespace ").Append(ns).Append(";\n\n");
        sb.Append("// The shared form model for the create + edit slices; maps onto ").Append(entity).Append(".Create/Update.\n");
        sb.Append("public sealed class ").Append(entity).Append("Request\n{\n");
        var first = true;
        foreach (var field in fields)
        {
            if (annotate && field.IsString)
            {
                if (!first)
                {
                    sb.Append('\n');
                }

                if (!field.IsNullable)
                {
                    sb.Append("    [Required]\n");
                }

                sb.Append("    [MaxLength(").Append(field.MaxLength!.Value.ToString(CultureInfo.InvariantCulture)).Append(")]\n");
            }

            sb.Append("    public ").Append(field.PropertyType).Append(' ').Append(field.Name).Append(" { get; set; }");
            if (field.Initializer is not null)
            {
                sb.Append(' ').Append(field.Initializer).Append(';');
            }

            sb.Append('\n');
            first = false;
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    // ---- generated tests (xunit): domain (pure) + persistence (SQLite round-trip) ----

    // Pure domain tests: Create sets every property, Update overwrites them, and each value object
    // rejects a blank value / accepts a valid one. No DB, no browser.
    private static string RenderDomainTests(string testNs, string featureNs, string entity, IReadOnlyList<FieldSpec> fields, bool useValueObjects)
    {
        var sb = new StringBuilder();
        sb.Append("using ").Append(featureNs).Append(";\n\n");
        sb.Append("namespace ").Append(testNs).Append(";\n\n");
        sb.Append("public sealed class ").Append(entity).Append("Tests\n{\n");

        sb.Append("    [Fact]\n");
        sb.Append("    public void Create_sets_every_property()\n    {\n");
        sb.Append("        var entity = ").Append(entity).Append(".Create(").Append(SampleArgs(fields, second: false)).Append(");\n\n");
        sb.Append(SampleAsserts("entity", fields, useValueObjects, second: false, indent: "        ")).Append("\n    }\n\n");

        sb.Append("    [Fact]\n");
        sb.Append("    public void Update_overwrites_every_property()\n    {\n");
        sb.Append("        var entity = ").Append(entity).Append(".Create(").Append(SampleArgs(fields, second: false)).Append(");\n\n");
        sb.Append("        entity.Update(").Append(SampleArgs(fields, second: true)).Append(");\n\n");
        sb.Append(SampleAsserts("entity", fields, useValueObjects, second: true, indent: "        ")).Append("\n    }\n");

        foreach (var field in fields.Where(f => IsValueObject(f, useValueObjects)))
        {
            var vo = ValueObjectName(entity, field);
            sb.Append("\n    [Fact]\n");
            sb.Append("    public void ").Append(vo).Append("_rejects_a_blank_value() => Assert.NotEmpty(").Append(vo).Append(".Validate(\"   \"));\n");
            sb.Append("\n    [Fact]\n");
            sb.Append("    public void ").Append(vo).Append("_accepts_a_valid_value() => Assert.Empty(").Append(vo).Append(".Validate(\"").Append(SampleString(field, second: false)).Append("\"));\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    // Persistence test: the entity round-trips through a real SQLite file, proving the configuration's
    // columns + value-object converters persist and rehydrate.
    private static string RenderPersistenceTests(string testNs, string featureNs, string entity, string plural, string context, string idType, IReadOnlyList<FieldSpec> fields, bool useValueObjects)
    {
        var sb = new StringBuilder();
        sb.Append("using Microsoft.EntityFrameworkCore;\n");
        sb.Append("using ").Append(featureNs).Append(";\n\n");
        sb.Append("namespace ").Append(testNs).Append(";\n\n");
        sb.Append("public sealed class ").Append(plural).Append("PersistenceTests : IDisposable\n{\n");
        sb.Append("    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $\"rask-test-{Guid.NewGuid():N}.db\");\n\n");
        sb.Append("    private ").Append(context).Append(" NewContext()\n    {\n");
        sb.Append("        var options = new DbContextOptionsBuilder<").Append(context).Append(">()\n");
        sb.Append("            .UseSqlite($\"Data Source={_dbPath}\")\n");
        sb.Append("            .Options;\n");
        sb.Append("        return new ").Append(context).Append("(options);\n    }\n\n");

        sb.Append("    [Fact]\n");
        sb.Append("    public async Task ").Append(entity).Append("_round_trips_through_sqlite()\n    {\n");
        sb.Append("        ").Append(idType).Append(" id;\n");
        sb.Append("        await using (var db = NewContext())\n        {\n");
        sb.Append("            await db.Database.EnsureCreatedAsync();\n");
        sb.Append("            var entity = ").Append(entity).Append(".Create(").Append(SampleArgs(fields, second: false)).Append(");\n");
        sb.Append("            db.").Append(plural).Append(".Add(entity);\n");
        sb.Append("            await db.SaveChangesAsync();\n");
        sb.Append("            id = entity.Id;\n        }\n\n");
        sb.Append("        await using (var db = NewContext())\n        {\n");
        sb.Append("            var entity = await db.").Append(plural).Append(".SingleAsync();\n");
        sb.Append("            Assert.Equal(id, entity.Id);\n");
        sb.Append(SampleAsserts("entity", fields, useValueObjects, second: false, indent: "            ")).Append("\n        }\n    }\n\n");

        sb.Append("    public void Dispose()\n    {\n");
        sb.Append("        if (File.Exists(_dbPath))\n        {\n");
        sb.Append("            File.Delete(_dbPath);\n        }\n    }\n}\n");
        return sb.ToString();
    }

    // The Create/Update argument list — one sample literal per field (the `second` set differs so an
    // Update visibly changes every value).
    private static string SampleArgs(IReadOnlyList<FieldSpec> fields, bool second) =>
        string.Join(", ", fields.Select(f => SampleLiteral(f, second)));

    // One assertion per field: value objects expose the primitive through `.Value`. Booleans use
    // Assert.True/False (xUnit2004 forbids Assert.Equal on a bool).
    private static string SampleAsserts(string entityVar, IReadOnlyList<FieldSpec> fields, bool useValueObjects, bool second, string indent) =>
        string.Join("\n", fields.Select(f =>
        {
            var access = IsValueObject(f, useValueObjects) ? $"{entityVar}.{f.Name}.Value" : $"{entityVar}.{f.Name}";
            if (f.CsType == "bool")
            {
                // The `first` sample is true, the `second` (update) sample is false.
                return $"{indent}Assert.{(second ? "False" : "True")}({access});";
            }

            return $"{indent}Assert.Equal({SampleLiteral(f, second)}, {access});";
        }));

    // A deterministic sample value per field type (two sets so create ≠ update); the string fits the
    // field's max length so value-object construction never rejects it.
    private static string SampleLiteral(FieldSpec f, bool second) => f.CsType switch
    {
        "string" => "\"" + SampleString(f, second) + "\"",
        "int" => second ? "2" : "1",
        "long" => second ? "2L" : "1L",
        "decimal" => second ? "20.50m" : "10.25m",
        "double" => second ? "2.5d" : "1.5d",
        "bool" => second ? "false" : "true",
        "DateTime" => second ? "new DateTime(2025, 6, 15)" : "new DateTime(2024, 1, 1)",
        "Guid" => second ? "Guid.Parse(\"22222222-2222-2222-2222-222222222222\")" : "Guid.Parse(\"11111111-1111-1111-1111-111111111111\")",
        _ => "default",
    };

    // A sample string for a field, trimmed to its max length so a value object's own validation accepts it.
    private static string SampleString(FieldSpec f, bool second)
    {
        var value = second ? "Updated" : "Sample";
        return f.MaxLength is int max && value.Length > max ? value[..max] : value;
    }

    // ---- per-field fragment builders ----

    private static string Assignments(IReadOnlyList<FieldSpec> fields, string indent) =>
        // `this.` disambiguates the property from an identically-cased parameter (e.g. a lowercase
        // field name `title` → `this.title = title;`), avoiding a CS1717 self-assignment.
        string.Join("\n", fields.Select(f => $"{indent}this.{f.Name} = {Identifiers.ToCamelCase(f.Name)};"));

    private static string RequestArgs(IReadOnlyList<FieldSpec> fields, string source) =>
        string.Join(", ", fields.Select(f => $"{source}.{f.Name}"));

    private static string TableHeaders(IReadOnlyList<FieldSpec> fields) =>
        string.Join("\n", fields.Select(f => $"                            Th()[\"{f.Name}\"],"));

    private static string TableCells(IReadOnlyList<FieldSpec> fields, bool useValueObjects) =>
        string.Join("\n", fields.Select(f =>
        {
            var access = IsValueObject(f, useValueObjects) ? $"x.{f.Name}.Value" : f.IsString ? $"x.{f.Name}" : $"$\"{{x.{f.Name}}}\"";
            return $"                            Td()[{access}],";
        }));

    private static string CopyToForm(IReadOnlyList<FieldSpec> fields, bool useValueObjects) =>
        string.Join("\n", fields.Select(f => $"                _form.{f.Name} = entity.{f.Name}{(IsValueObject(f, useValueObjects) ? ".Value" : "")};"));

    // The EF Core mapping per string column: a value object maps through its converter; a primitive
    // string gets IsRequired()/HasMaxLength(). Other types need no configuration.
    private static string ConfigProperties(string entity, IReadOnlyList<FieldSpec> fields, bool useValueObjects) =>
        string.Join("\n", fields.Where(f => f.IsString).Select(f =>
        {
            if (IsValueObject(f, useValueObjects))
            {
                // Length comes from the value object's own MaxLength — a single source of truth.
                var vo = ValueObjectName(entity, f);
                return $"        entity.Property(x => x.{f.Name}).HasConversion(v => v.Value, s => {vo}.Create(s)).HasMaxLength({vo}.MaxLength);";
            }

            var required = f.IsNullable ? "" : ".IsRequired()";
            var len = f.MaxLength!.Value.ToString(CultureInfo.InvariantCulture);
            return $"        entity.Property(x => x.{f.Name}){required}.HasMaxLength({len});";
        }));

    private static string FormFields(string entity, IReadOnlyList<FieldSpec> fields, bool useValueObjects, bool useBs)
    {
        var sb = new StringBuilder();
        foreach (var field in fields)
        {
            var id = field.Name.ToLowerInvariant();
            // A value-object field wires its built-in Validate into the bound input; the dataannotations /
            // fluent modes validate through the form-level validator component instead.
            var validate = IsValueObject(field, useValueObjects) ? $", Validate: {ValueObjectName(entity, field)}.Validate" : "";
            if (useBs)
            {
                // Bs form controls render their own label + input + validation feedback.
                var control = field.CsType == "bool" ? "BsCheck" : "BsInput";
                sb.Append("                    ").Append(control).Append("(() => _form.").Append(field.Name).Append(validate)
                    .Append(", Id: \"").Append(id).Append("\", Label: \"").Append(field.Name).Append("\"),\n");
            }
            else
            {
                // Plain, unstyled HTML: a label + the bound input (a bool renders as a checkbox).
                sb.Append("                    Div()[\n")
                    .Append("                        Label(\"").Append(id).Append("\")[\"").Append(field.Name).Append("\"],\n")
                    .Append("                        Input(() => _form.").Append(field.Name).Append(validate).Append(", Id: \"").Append(id).Append("\")\n")
                    .Append("                    ],\n");
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    // The NuGet packages the generated slice references — the command adds these to the project.
    private static IReadOnlyList<string> FeaturePackages(string validation, bool useBs)
    {
        var packages = new List<string>
        {
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.EntityFrameworkCore.Design",
            "Rask.Cqrs",
        };

        if (useBs)
        {
            packages.Add("Rask.Bootstrap");
        }

        if (validation == "dataannotations")
        {
            packages.Add("Rask.Validation.DataAnnotations");
        }
        else if (validation == "fluent")
        {
            packages.Add("Rask.Validation.FluentValidation");
        }

        return packages;
    }

    private static string RenderNextSteps(string context, string entity, string plural, string route, bool generatedContext, string validation, bool useBs, bool generatedTests)
    {
        var steps = new StringBuilder();
        steps.Append("Next steps:\n");
        steps.Append("  1. The required packages were added for you (EF Core + SQLite, Rask.Cqrs");
        if (useBs)
        {
            steps.Append(", Rask.Bootstrap");
        }

        if (validation == "dataannotations")
        {
            steps.Append(", Rask.Validation.DataAnnotations");
        }
        else if (validation == "fluent")
        {
            steps.Append(", Rask.Validation.FluentValidation");
        }

        steps.Append(").\n");
        if (useBs)
        {
            steps.Append("     Link BootstrapStyles() in your Head.\n");
        }

        steps.Append("  2. Register services in Program.cs:\n");
        steps.Append("       builder.Services.AddRaskCqrs();\n");
        if (generatedContext)
        {
            steps.Append("       builder.Services.AddDbContextFactory<").Append(context).Append(">(o => o.UseSqlite(\"Data Source=app.db\"));\n");
        }
        else
        {
            steps.Append("       // in your ").Append(context).Append(": add `public DbSet<").Append(entity).Append("> ").Append(plural).Append(" => Set<").Append(entity).Append(">();`\n");
            steps.Append("       // and apply the config: modelBuilder.ApplyConfigurationsFromAssembly(typeof(").Append(entity).Append("Configuration).Assembly);\n");
        }

        steps.Append("  3. Create the schema (EF Core migrations):\n");
        steps.Append("       dotnet ef migrations add Add").Append(entity).Append(" && dotnet ef database update\n");
        steps.Append("  4. Run the app and browse to ").Append(route).Append('.');
        if (generatedTests)
        {
            steps.Append("\n  5. The generated tests live in a sibling <Project>.Tests project — it needs xunit,\n");
            steps.Append("     Microsoft.NET.Test.Sdk");
            if (generatedContext)
            {
                steps.Append(" + Microsoft.EntityFrameworkCore.Sqlite");
            }

            steps.Append(", and a project reference to this app. Then: dotnet test");
        }

        return steps.ToString();
    }

    private static string Apply(string template, (string Token, string Value)[] replacements)
    {
        foreach (var (token, value) in replacements)
        {
            template = template.Replace(token, value, StringComparison.Ordinal);
        }

        return template;
    }

    // ---- vertical-slice templates (token replacement keeps generated `$"…"` / `[Route("{id:…}")]` literal) ----

    private const string DbContextTemplate =
        """
        using Microsoft.EntityFrameworkCore;

        namespace __NS__;

        public sealed class __CONTEXT__(DbContextOptions<__CONTEXT__> options) : DbContext(options)
        {
            public DbSet<__ENTITY__> __PLURAL__ => Set<__ENTITY__>();

            protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                modelBuilder.ApplyConfigurationsFromAssembly(typeof(__CONTEXT__).Assembly);
        }

        """;

    private const string ValueObjectTemplate =
        """
        namespace __NS__;

        // Value object for __FIELD__ — the validation rule lives here and is reused by the form
        // (Input(..., Validate: __VO__.Validate)) and by Create.
        public readonly record struct __VO__
        {
            public const int MaxLength = __MAX__;

            public string Value { get; }

            private __VO__(string value) => Value = value;

            public static IEnumerable<string> Validate(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    yield return "__FIELD__ is required.";
                }
                else if (value.Trim().Length > MaxLength)
                {
                    yield return $"__FIELD__ must be {MaxLength} characters or fewer.";
                }
            }

            public static __VO__ Create(string value)
            {
                var errors = Validate(value).ToList();
                if (errors.Count > 0)
                {
                    throw new ArgumentException(string.Join(" ", errors), nameof(value));
                }

                return new __VO__(value.Trim());
            }

            public override string ToString() => Value;
        }

        """;

    private const string FluentValidatorTemplate =
        """
        using FluentValidation;

        namespace __NS__;

        public sealed class __ENTITY__RequestValidator : AbstractValidator<__ENTITY__Request>
        {
            public __ENTITY__RequestValidator()
            {
        __RULES__
            }
        }

        """;

    private const string ConfigurationTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace __NS__;

        // The EF Core mapping for __ENTITY__ (keeps the domain model free of persistence attributes).
        public sealed class __ENTITY__Configuration : IEntityTypeConfiguration<__ENTITY__>
        {
            public void Configure(EntityTypeBuilder<__ENTITY__> entity)
            {
                entity.HasKey(x => x.Id);
        __CONFIGPROPS__
            }
        }

        """;

    private const string ListPageTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record List__PLURAL__Query : IQuery<IReadOnlyList<__ENTITY__>>;

        public sealed class List__PLURAL__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<List__PLURAL__Query, IReadOnlyList<__ENTITY__>>
        {
            public async Task<IReadOnlyList<__ENTITY__>> HandleAsync(List__PLURAL__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
            }
        }

        [Route("__ROUTE__")]
        public sealed class __PLURAL__Page(IDispatcher dispatcher) : Component
        {
            private IReadOnlyList<__ENTITY__> _items = [];
            private bool _loaded;

            protected override Component? Head => Title()["__PLURAL__"];

            protected override async Task OnMountAsync() => await LoadAsync();

            private async Task LoadAsync()
            {
                _items = await dispatcher.DispatchAsync(new List__PLURAL__Query(), CancellationToken);
                _loaded = true;
            }

            protected override Component? Render() =>
            [
                Div()[
                    H1()["__PLURAL__"],
                    NavLink(Routes.Create__ENTITY__())["New __ENTITY__"]
                ],
                !_loaded
                    ? Div()["Loading…"]
                    : _items.Count == 0
                        ? Div()["No __PLURAL__ yet."]
                        : Table()[
                            Thead()[
                                Tr()[
                                    Th()["#"],
        __HEADERS__
                                    Th()[""]
                                ]
                            ],
                            Tbody()[
                                _items.Select(x => Tr(Key: x.Id)[
                                    Td()[$"{x.Id}"],
        __CELLS__
                                    Td()[
                                        NavLink(Routes.Update__ENTITY__(x.Id))["Edit"],
                                        Delete__ENTITY__(Id: x.Id, OnDeleted: LoadAsync)
                                    ]
                                ])
                            ]
                        ]
            ];
        }

        """;

    private const string DeleteTemplate =
        """
        using Microsoft.EntityFrameworkCore;

        namespace __NS__;

        public sealed record Delete__ENTITY__Command(__IDTYPE__ Id) : ICommand;

        public sealed class Delete__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Delete__ENTITY__Command>
        {
            public async Task HandleAsync(Delete__ENTITY__Command command, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                await db.__PLURAL__.Where(x => x.Id == command.Id).ExecuteDeleteAsync(cancellationToken);
            }
        }

        // A reusable delete button: dispatches the delete command, then invokes OnDeleted so the caller
        // (the list page) can refresh.
        public sealed class Delete__ENTITY__(IDispatcher dispatcher) : Component
        {
            public __IDTYPE__ Id { get; set; }

            public Func<Task>? OnDeleted { get; set; }

            private async Task DeleteAsync()
            {
                await dispatcher.DispatchAsync(new Delete__ENTITY__Command(Id), CancellationToken);
                if (OnDeleted is not null)
                {
                    await OnDeleted();
                }
            }

            protected override Component? Render() =>
                Button("button", OnClickAsync: DeleteAsync)["Delete"];
        }

        """;

    private const string CreateTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record Create__ENTITY__Command(__ENTITY__Request Request) : ICommand<__IDTYPE__>;

        public sealed class Create__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Create__ENTITY__Command, __IDTYPE__>
        {
            public async Task<__IDTYPE__> HandleAsync(Create__ENTITY__Command command, CancellationToken cancellationToken)
            {
                var entity = __ENTITY__.Create(__CREATEARGS__);
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                db.__PLURAL__.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                return entity.Id;
            }
        }

        [Route("__ROUTE__/new")]
        public sealed class Create__ENTITY__(IDispatcher dispatcher, Navigator navigator) : Component
        {
            private readonly __ENTITY__Request _form = new();

            protected override Component? Head => Title()["New __ENTITY__"];

            private async Task SubmitAsync(__ENTITY__Request form)
            {
                await dispatcher.DispatchAsync(new Create__ENTITY__Command(form), CancellationToken);
                navigator.NavigateTo(Routes.__PLURAL__Page());
            }

            protected override Component? Render() =>
                Div()[
                    Div()[
                        H1()["New __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync)[
        __VALIDATOR____FORMFIELDS__
                            Div()[
                                NavLink(Routes.__PLURAL__Page())["Cancel"],
                                Button("submit")["Save"]
                            ]
                        ]
                    ]
                ];
        }

        """;

    private const string UpdateTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record Get__ENTITY__Query(__IDTYPE__ Id) : IQuery<__ENTITY__?>;

        public sealed class Get__ENTITY__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<Get__ENTITY__Query, __ENTITY__?>
        {
            public async Task<__ENTITY__?> HandleAsync(Get__ENTITY__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
            }
        }

        public sealed record Update__ENTITY__Command(__IDTYPE__ Id, __ENTITY__Request Request) : ICommand;

        public sealed class Update__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Update__ENTITY__Command>
        {
            public async Task HandleAsync(Update__ENTITY__Command command, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await db.__PLURAL__.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (entity is null)
                {
                    return;
                }

                entity.Update(__CREATEARGS__);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        [Route("__ROUTE__/{id:__IDCONSTRAINT__}/edit")]
        public sealed class Update__ENTITY__(IDispatcher dispatcher, Navigator navigator) : Component
        {
            private readonly __ENTITY__Request _form = new();
            private bool _loaded;
            private bool _found;

            [RouteParam] public __IDTYPE__ Id { get; set; }

            protected override Component? Head => Title()["Edit __ENTITY__"];

            protected override async Task OnPropsChangedAsync()
            {
                _loaded = false;
                var entity = await dispatcher.DispatchAsync(new Get__ENTITY__Query(Id), CancellationToken);
                _found = entity is not null;
                if (entity is not null)
                {
        __COPYTOFORM__
                }

                _loaded = true;
            }

            private async Task SubmitAsync(__ENTITY__Request form)
            {
                await dispatcher.DispatchAsync(new Update__ENTITY__Command(Id, form), CancellationToken);
                navigator.NavigateTo(Routes.__PLURAL__Page());
            }

            protected override Component? Render()
            {
                if (!_loaded)
                {
                    return Div()["Loading…"];
                }

                if (!_found)
                {
                    return Div()["__ENTITY__ not found. ", NavLink(Routes.__PLURAL__Page())["Back to the list"], "."];
                }

                return Div()[
                    Div()[
                        H1()["Edit __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync)[
        __VALIDATOR____FORMFIELDS__
                            Div()[
                                NavLink(Routes.__PLURAL__Page())["Cancel"],
                                Button("submit")["Save changes"]
                            ]
                        ]
                    ]
                ];
            }
        }

        """;

    // ---- Bs (Rask.Bootstrap) variants: same CQRS, Bs components + Bs.Join utility classes in the render ----

    private const string BsListPageTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record List__PLURAL__Query : IQuery<IReadOnlyList<__ENTITY__>>;

        public sealed class List__PLURAL__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<List__PLURAL__Query, IReadOnlyList<__ENTITY__>>
        {
            public async Task<IReadOnlyList<__ENTITY__>> HandleAsync(List__PLURAL__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
            }
        }

        [Route("__ROUTE__")]
        public sealed class __PLURAL__Page(IDispatcher dispatcher, Navigator navigator) : Component
        {
            private IReadOnlyList<__ENTITY__> _items = [];
            private bool _loaded;

            protected override Component? Head => Title()["__PLURAL__"];

            protected override async Task OnMountAsync() => await LoadAsync();

            private async Task LoadAsync()
            {
                _items = await dispatcher.DispatchAsync(new List__PLURAL__Query(), CancellationToken);
                _loaded = true;
            }

            protected override Component? Render() =>
            [
                Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center), Margin.Bottom(3)))[
                    H1(Class: "h3 mb-0")["__PLURAL__"],
                    BsButton(Color: BsColor.Primary, OnClick: () => navigator.NavigateTo(Routes.Create__ENTITY__()))[
                        BsIcon(Name: BsIconName.PlusLg, Class: Margin.End(1)), "New __ENTITY__"
                    ]
                ],
                !_loaded
                    ? Div(Class: Bs.Join(Txt.Muted))["Loading…"]
                    : _items.Count == 0
                        ? Div(Class: "alert alert-info")["No __PLURAL__ yet."]
                        : BsTable(Striped: true, Hover: true, Responsive: true)[
                            Thead()[
                                Tr()[
                                    Th()["#"],
        __HEADERS__
                                    Th()[""]
                                ]
                            ],
                            Tbody()[
                                _items.Select(x => Tr(Key: x.Id)[
                                    Td()[$"{x.Id}"],
        __CELLS__
                                    Td(Class: Bs.Join(Txt.End(), Txt.Nowrap))[
                                        BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClick: () => navigator.NavigateTo(Routes.Update__ENTITY__(x.Id)))[BsIcon(Name: BsIconName.Pencil)],
                                        Delete__ENTITY__(Id: x.Id, OnDeleted: LoadAsync)
                                    ]
                                ])
                            ]
                        ]
            ];
        }

        """;

    private const string BsDeleteTemplate =
        """
        using Microsoft.EntityFrameworkCore;

        namespace __NS__;

        public sealed record Delete__ENTITY__Command(__IDTYPE__ Id) : ICommand;

        public sealed class Delete__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Delete__ENTITY__Command>
        {
            public async Task HandleAsync(Delete__ENTITY__Command command, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                await db.__PLURAL__.Where(x => x.Id == command.Id).ExecuteDeleteAsync(cancellationToken);
            }
        }

        public sealed class Delete__ENTITY__(IDispatcher dispatcher) : Component
        {
            public __IDTYPE__ Id { get; set; }

            public Func<Task>? OnDeleted { get; set; }

            private async Task DeleteAsync()
            {
                await dispatcher.DispatchAsync(new Delete__ENTITY__Command(Id), CancellationToken);
                if (OnDeleted is not null)
                {
                    await OnDeleted();
                }
            }

            protected override Component? Render() =>
                BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, OnClickAsync: DeleteAsync)[BsIcon(Name: BsIconName.Trash)];
        }

        """;

    private const string BsCreateTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record Create__ENTITY__Command(__ENTITY__Request Request) : ICommand<__IDTYPE__>;

        public sealed class Create__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Create__ENTITY__Command, __IDTYPE__>
        {
            public async Task<__IDTYPE__> HandleAsync(Create__ENTITY__Command command, CancellationToken cancellationToken)
            {
                var entity = __ENTITY__.Create(__CREATEARGS__);
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                db.__PLURAL__.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                return entity.Id;
            }
        }

        [Route("__ROUTE__/new")]
        public sealed class Create__ENTITY__(IDispatcher dispatcher, Navigator navigator) : Component
        {
            private readonly __ENTITY__Request _form = new();

            protected override Component? Head => Title()["New __ENTITY__"];

            private async Task SubmitAsync(__ENTITY__Request form)
            {
                await dispatcher.DispatchAsync(new Create__ENTITY__Command(form), CancellationToken);
                navigator.NavigateTo(Routes.__PLURAL__Page());
            }

            protected override Component? Render() =>
                BsCard(Class: Bs.Join(Shadow.Sm, Border.None, "mx-auto"))[
                    BsCardBody()[
                        H1(Class: "h4 mb-3")["New __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync, Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3)))[
        __VALIDATOR____FORMFIELDS__
                            Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.End), Flex.Gap(2)))[
                                BsButton(Color: BsColor.Secondary, Outline: true, OnClick: () => navigator.NavigateTo(Routes.__PLURAL__Page()))["Cancel"],
                                BsButton(Type: "submit", Color: BsColor.Primary)["Save"]
                            ]
                        ]
                    ]
                ];
        }

        """;

    private const string BsUpdateTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record Get__ENTITY__Query(__IDTYPE__ Id) : IQuery<__ENTITY__?>;

        public sealed class Get__ENTITY__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<Get__ENTITY__Query, __ENTITY__?>
        {
            public async Task<__ENTITY__?> HandleAsync(Get__ENTITY__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
            }
        }

        public sealed record Update__ENTITY__Command(__IDTYPE__ Id, __ENTITY__Request Request) : ICommand;

        public sealed class Update__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Update__ENTITY__Command>
        {
            public async Task HandleAsync(Update__ENTITY__Command command, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await db.__PLURAL__.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (entity is null)
                {
                    return;
                }

                entity.Update(__CREATEARGS__);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        [Route("__ROUTE__/{id:__IDCONSTRAINT__}/edit")]
        public sealed class Update__ENTITY__(IDispatcher dispatcher, Navigator navigator) : Component
        {
            private readonly __ENTITY__Request _form = new();
            private bool _loaded;
            private bool _found;

            [RouteParam] public __IDTYPE__ Id { get; set; }

            protected override Component? Head => Title()["Edit __ENTITY__"];

            protected override async Task OnPropsChangedAsync()
            {
                _loaded = false;
                var entity = await dispatcher.DispatchAsync(new Get__ENTITY__Query(Id), CancellationToken);
                _found = entity is not null;
                if (entity is not null)
                {
        __COPYTOFORM__
                }

                _loaded = true;
            }

            private async Task SubmitAsync(__ENTITY__Request form)
            {
                await dispatcher.DispatchAsync(new Update__ENTITY__Command(Id, form), CancellationToken);
                navigator.NavigateTo(Routes.__PLURAL__Page());
            }

            protected override Component? Render()
            {
                if (!_loaded)
                {
                    return Div(Class: Bs.Join(Txt.Muted))["Loading…"];
                }

                if (!_found)
                {
                    return Div(Class: "alert alert-warning")["__ENTITY__ not found. ", NavLink(Routes.__PLURAL__Page())["Back to the list"], "."];
                }

                return BsCard(Class: Bs.Join(Shadow.Sm, Border.None, "mx-auto"))[
                    BsCardBody()[
                        H1(Class: "h4 mb-3")["Edit __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync, Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3)))[
        __VALIDATOR____FORMFIELDS__
                            Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.End), Flex.Gap(2)))[
                                BsButton(Color: BsColor.Secondary, Outline: true, OnClick: () => navigator.NavigateTo(Routes.__PLURAL__Page()))["Cancel"],
                                BsButton(Type: "submit", Color: BsColor.Primary)["Save changes"]
                            ]
                        ]
                    ]
                ];
            }
        }

        """;

    // --modal: the list page holds the whole slice (list/get/create/update CQRS) and edits in a BsModal.
    private const string BsModalListTemplate =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Core.Routing;

        namespace __NS__;

        public sealed record List__PLURAL__Query : IQuery<IReadOnlyList<__ENTITY__>>;

        public sealed class List__PLURAL__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<List__PLURAL__Query, IReadOnlyList<__ENTITY__>>
        {
            public async Task<IReadOnlyList<__ENTITY__>> HandleAsync(List__PLURAL__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
            }
        }

        public sealed record Get__ENTITY__Query(__IDTYPE__ Id) : IQuery<__ENTITY__?>;

        public sealed class Get__ENTITY__QueryHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : IQueryHandler<Get__ENTITY__Query, __ENTITY__?>
        {
            public async Task<__ENTITY__?> HandleAsync(Get__ENTITY__Query query, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await db.__PLURAL__.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
            }
        }

        public sealed record Create__ENTITY__Command(__ENTITY__Request Request) : ICommand<__IDTYPE__>;

        public sealed class Create__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Create__ENTITY__Command, __IDTYPE__>
        {
            public async Task<__IDTYPE__> HandleAsync(Create__ENTITY__Command command, CancellationToken cancellationToken)
            {
                var entity = __ENTITY__.Create(__CREATEARGS__);
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                db.__PLURAL__.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
                return entity.Id;
            }
        }

        public sealed record Update__ENTITY__Command(__IDTYPE__ Id, __ENTITY__Request Request) : ICommand;

        public sealed class Update__ENTITY__CommandHandler(IDbContextFactory<__CONTEXT__> dbContextFactory)
            : ICommandHandler<Update__ENTITY__Command>
        {
            public async Task HandleAsync(Update__ENTITY__Command command, CancellationToken cancellationToken)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await db.__PLURAL__.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (entity is null)
                {
                    return;
                }

                entity.Update(__CREATEARGS__);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        [Route("__ROUTE__")]
        public sealed class __PLURAL__Page(IDispatcher dispatcher) : Component
        {
            private IReadOnlyList<__ENTITY__> _items = [];
            private bool _loaded;
            private __ENTITY__Request _form = new();
            private bool _modalOpen;
            private __IDTYPE__? _editingId;

            protected override Component? Head => Title()["__PLURAL__"];

            protected override async Task OnMountAsync() => await LoadAsync();

            private async Task LoadAsync()
            {
                _items = await dispatcher.DispatchAsync(new List__PLURAL__Query(), CancellationToken);
                _loaded = true;
            }

            private void OpenCreate()
            {
                _form = new __ENTITY__Request();
                _editingId = null;
                _modalOpen = true;
            }

            private async Task OpenEditAsync(__IDTYPE__ id)
            {
                var entity = await dispatcher.DispatchAsync(new Get__ENTITY__Query(id), CancellationToken);
                if (entity is null)
                {
                    return;
                }

                _form = new __ENTITY__Request();
        __COPYTOFORM__
                _editingId = id;
                _modalOpen = true;
            }

            private void CloseModal() => _modalOpen = false;

            private async Task SaveAsync(__ENTITY__Request form)
            {
                if (_editingId is null)
                {
                    await dispatcher.DispatchAsync(new Create__ENTITY__Command(form), CancellationToken);
                }
                else
                {
                    await dispatcher.DispatchAsync(new Update__ENTITY__Command(_editingId.Value, form), CancellationToken);
                }

                _modalOpen = false;
                await LoadAsync();
            }

            protected override Component? Render() =>
            [
                Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center), Margin.Bottom(3)))[
                    H1(Class: "h3 mb-0")["__PLURAL__"],
                    BsButton(Color: BsColor.Primary, OnClick: OpenCreate)[
                        BsIcon(Name: BsIconName.PlusLg, Class: Margin.End(1)), "New __ENTITY__"
                    ]
                ],
                !_loaded
                    ? Div(Class: Bs.Join(Txt.Muted))["Loading…"]
                    : _items.Count == 0
                        ? Div(Class: "alert alert-info")["No __PLURAL__ yet."]
                        : BsTable(Striped: true, Hover: true, Responsive: true)[
                            Thead()[
                                Tr()[
                                    Th()["#"],
        __HEADERS__
                                    Th()[""]
                                ]
                            ],
                            Tbody()[
                                _items.Select(x => Tr(Key: x.Id)[
                                    Td()[$"{x.Id}"],
        __CELLS__
                                    Td(Class: Bs.Join(Txt.End(), Txt.Nowrap))[
                                        BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClickAsync: () => OpenEditAsync(x.Id))[BsIcon(Name: BsIconName.Pencil)],
                                        Delete__ENTITY__(Id: x.Id, OnDeleted: LoadAsync)
                                    ]
                                ])
                            ]
                        ],
                BsModal(Open: _modalOpen, Title: _editingId is null ? "New __ENTITY__" : "Edit __ENTITY__", Centered: true, OnClose: CloseModal)[
                    Form(_form, OnValidSubmitAsync: SaveAsync, Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3)))[
        __VALIDATOR____FORMFIELDS__
                        Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.End), Flex.Gap(2)))[
                            BsButton(Color: BsColor.Secondary, Outline: true, OnClick: CloseModal)["Cancel"],
                            BsButton(Type: "submit", Color: BsColor.Primary)["Save"]
                        ]
                    ]
                ]
            ];
        }

        """;
}
