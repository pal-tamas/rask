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
        string? contextOverride,
        string? pluralOverride,
        string? outputOverride)
    {
        var plural = pluralOverride ?? Pluralizer.Pluralize(entityName);
        var route = Identifiers.ToRoutePath(plural);
        var idConstraint = idType == "Guid" ? "guid" : idType;
        var generateContext = contextOverride is null;
        var context = contextOverride ?? plural + "DbContext";

        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Features", plural);
        var ns = project.NamespaceFor(targetDirectory);

        var tokens = new (string, string)[]
        {
            ("__NS__", ns), ("__ENTITY__", entityName), ("__PLURAL__", plural), ("__CONTEXT__", context),
            ("__ROUTE__", route), ("__IDTYPE__", idType), ("__IDCONSTRAINT__", idConstraint),
            ("__CREATEARGS__", RequestArgs(fields, "command.Request")),
            ("__HEADERS__", TableHeaders(fields)), ("__CELLS__", TableCells(fields)),
            ("__FORMFIELDS__", FormFields(entityName, fields)), ("__COPYTOFORM__", CopyToForm(fields)),
            ("__CONFIGPROPS__", ConfigProperties(entityName, fields)),
        };

        var files = new List<ScaffoldFile>
        {
            new(Path.Combine(targetDirectory, entityName + ".cs"), RenderEntity(ns, entityName, fields, idType)),
            new(Path.Combine(targetDirectory, entityName + "Request.cs"), RenderRequest(ns, entityName, fields)),
            new(Path.Combine(targetDirectory, entityName + "Configuration.cs"), Apply(ConfigurationTemplate, tokens)),
            new(Path.Combine(targetDirectory, plural + "Page.cs"), Apply(ListPageTemplate, tokens)),
            new(Path.Combine(targetDirectory, "Delete" + entityName + ".cs"), Apply(DeleteTemplate, tokens)),
            new(Path.Combine(targetDirectory, "Create" + entityName + ".cs"), Apply(CreateTemplate, tokens)),
            new(Path.Combine(targetDirectory, "Update" + entityName + ".cs"), Apply(UpdateTemplate, tokens)),
        };

        // One value object per required-string field, each owning its validation.
        foreach (var field in fields.Where(IsValueObject))
        {
            files.Add(new ScaffoldFile(
                Path.Combine(targetDirectory, ValueObjectName(entityName, field) + ".cs"),
                RenderValueObject(ns, entityName, field)));
        }

        if (generateContext)
        {
            files.Insert(2, new ScaffoldFile(Path.Combine(targetDirectory, context + ".cs"), Apply(DbContextTemplate, tokens)));
        }

        return new ScaffoldResult(files, RenderNextSteps(context, entityName, plural, route, generateContext));
    }

    // ---- entity + shared request (StringBuilder — per-field attributes make raw strings awkward) ----

    // A required (non-nullable) string field is modelled as a value object that owns its own validation
    // (built-in, dependency-free). Optional strings and other types stay primitive.
    private static bool IsValueObject(FieldSpec f) => f.IsString && !f.IsNullable;

    private static string ValueObjectName(string entity, FieldSpec f) => entity + f.Name;

    private static string EntityPropertyType(string entity, FieldSpec f) =>
        IsValueObject(f) ? ValueObjectName(entity, f) : f.PropertyType;

    internal static string RenderEntity(string ns, string entity, IReadOnlyList<FieldSpec> fields, string idType)
    {
        var sb = new StringBuilder();
        sb.Append("namespace ").Append(ns).Append(";\n\n");
        sb.Append("public sealed class ").Append(entity).Append("\n{\n");
        sb.Append("    private ").Append(entity).Append("() { } // EF Core materialization\n\n");

        // The ctor takes the value-object types; Create/Update take primitives and wrap them via From.
        var ctorParams = string.Join(", ", fields.Select(f => $"{EntityPropertyType(entity, f)} {Identifiers.ToCamelCase(f.Name)}"));
        sb.Append("    private ").Append(entity).Append('(').Append(ctorParams).Append(")\n    {\n");
        sb.Append(Assignments(fields, "        ")).Append("\n    }\n\n");
        sb.Append("    public ").Append(idType).Append(" Id { get; private set; }");
        sb.Append(idType == "Guid" ? " = Guid.NewGuid();\n" : "\n");

        foreach (var field in fields)
        {
            sb.Append("\n    public ").Append(EntityPropertyType(entity, field)).Append(' ').Append(field.Name).Append(" { get; private set; }");
            if (field.Initializer is not null && !IsValueObject(field))
            {
                sb.Append(' ').Append(field.Initializer).Append(';');
            }

            sb.Append('\n');
        }

        var createParams = string.Join(", ", fields.Select(f => $"{f.PropertyType} {Identifiers.ToCamelCase(f.Name)}"));
        var createArgs = string.Join(", ", fields.Select(f => WrapPrimitive(entity, f)));
        sb.Append("\n    public static ").Append(entity).Append(" Create(").Append(createParams).Append(") => new(").Append(createArgs).Append(");\n\n");

        sb.Append("    public void Update(").Append(createParams).Append(")\n    {\n");
        sb.Append(string.Join("\n", fields.Select(f => $"        this.{f.Name} = {WrapPrimitive(entity, f)};"))).Append("\n    }\n}\n");
        return sb.ToString();
    }

    // Wrap a primitive parameter into its value object where the field has one, else pass it through.
    private static string WrapPrimitive(string entity, FieldSpec f)
    {
        var param = Identifiers.ToCamelCase(f.Name);
        return IsValueObject(f) ? $"{ValueObjectName(entity, f)}.Create({param})" : param;
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

    private static string RenderRequest(string ns, string entity, IReadOnlyList<FieldSpec> fields)
    {
        var sb = new StringBuilder();
        sb.Append("namespace ").Append(ns).Append(";\n\n");
        sb.Append("// The shared form model for the create + edit slices; maps onto ").Append(entity).Append(".Create/Update.\n");
        sb.Append("// Validation lives on the value objects and runs in the form via Input(..., Validate: ...).\n");
        sb.Append("public sealed class ").Append(entity).Append("Request\n{\n");
        foreach (var field in fields)
        {
            sb.Append("    public ").Append(field.PropertyType).Append(' ').Append(field.Name).Append(" { get; set; }");
            if (field.Initializer is not null)
            {
                sb.Append(' ').Append(field.Initializer).Append(';');
            }

            sb.Append('\n');
        }

        sb.Append("}\n");
        return sb.ToString();
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

    private static string TableCells(IReadOnlyList<FieldSpec> fields) =>
        string.Join("\n", fields.Select(f =>
        {
            var access = IsValueObject(f) ? $"x.{f.Name}.Value" : f.IsString ? $"x.{f.Name}" : $"$\"{{x.{f.Name}}}\"";
            return $"                            Td()[{access}],";
        }));

    private static string CopyToForm(IReadOnlyList<FieldSpec> fields) =>
        string.Join("\n", fields.Select(f => $"                _form.{f.Name} = entity.{f.Name}{(IsValueObject(f) ? ".Value" : "")};"));

    // The EF Core mapping per string column: a value object maps through its converter; an optional
    // string just gets a length. Other types need no configuration.
    private static string ConfigProperties(string entity, IReadOnlyList<FieldSpec> fields) =>
        string.Join("\n", fields.Where(f => f.IsString).Select(f =>
        {
            if (IsValueObject(f))
            {
                // Length comes from the value object's own MaxLength — a single source of truth.
                var vo = ValueObjectName(entity, f);
                return $"        entity.Property(x => x.{f.Name}).HasConversion(v => v.Value, s => {vo}.Create(s)).HasMaxLength({vo}.MaxLength);";
            }

            var len = f.MaxLength!.Value.ToString(CultureInfo.InvariantCulture);
            return $"        entity.Property(x => x.{f.Name}).HasMaxLength({len});";
        }));

    private static string FormFields(string entity, IReadOnlyList<FieldSpec> fields)
    {
        var sb = new StringBuilder();
        foreach (var field in fields)
        {
            var id = field.Name.ToLowerInvariant();
            // A value-object field wires its built-in Validate into the bound input.
            var validate = IsValueObject(field) ? $", Validate: {ValueObjectName(entity, field)}.Validate" : "";
            if (field.CsType == "bool")
            {
                sb.Append("                    Div(Class: \"form-check\")[\n")
                    .Append("                        Input(() => _form.").Append(field.Name).Append(", Id: \"").Append(id).Append("\", Class: \"form-check-input\"),\n")
                    .Append("                        Label(\"").Append(id).Append("\", Class: \"form-check-label\")[\"").Append(field.Name).Append("\"]\n")
                    .Append("                    ],\n");
            }
            else
            {
                sb.Append("                    Div()[\n")
                    .Append("                        Label(\"").Append(id).Append("\", Class: \"form-label small mb-1\")[\"").Append(field.Name).Append("\"],\n")
                    .Append("                        Input(() => _form.").Append(field.Name).Append(validate).Append(", Id: \"").Append(id).Append("\", Class: \"form-control\")\n")
                    .Append("                    ],\n");
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string RenderNextSteps(string context, string entity, string plural, string route, bool generatedContext)
    {
        var steps = new StringBuilder();
        steps.Append("Next steps:\n");
        steps.Append("  1. Reference EF Core + SQLite and Rask.Cqrs if the project doesn't already:\n");
        steps.Append("       dotnet add package Microsoft.EntityFrameworkCore.Sqlite\n");
        steps.Append("       dotnet add package Microsoft.EntityFrameworkCore.Design\n");
        steps.Append("       dotnet add package Rask.Cqrs\n");
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
                Div(Class: "d-flex justify-content-between align-items-center mb-3")[
                    H1(Class: "h3 mb-0")["__PLURAL__"],
                    NavLink(Routes.Create__ENTITY__(), Class: "btn btn-primary")["New __ENTITY__"]
                ],
                !_loaded
                    ? Div(Class: "text-secondary")["Loading…"]
                    : _items.Count == 0
                        ? Div(Class: "alert alert-info")["No __PLURAL__ yet."]
                        : Table(Class: "table table-striped align-middle")[
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
                                    Td(Class: "text-end text-nowrap")[
                                        NavLink(Routes.Update__ENTITY__(x.Id), Class: "btn btn-outline-secondary btn-sm me-1")["Edit"],
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
                Button("button", Class: "btn btn-outline-danger btn-sm", OnClickAsync: DeleteAsync)["Delete"];
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
                Div(Class: "card shadow-sm border-0 mx-auto", Style: "max-width: 32rem")[
                    Div(Class: "card-body")[
                        H1(Class: "h4 mb-3")["New __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
        __FORMFIELDS__
                            Div(Class: "d-flex justify-content-end gap-2 pt-2")[
                                NavLink(Routes.__PLURAL__Page(), Class: "btn btn-outline-secondary")["Cancel"],
                                Button("submit", Class: "btn btn-primary")["Save"]
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
                    return Div(Class: "text-secondary")["Loading…"];
                }

                if (!_found)
                {
                    return Div(Class: "alert alert-warning")["__ENTITY__ not found. ", NavLink(Routes.__PLURAL__Page())["Back to the list"], "."];
                }

                return Div(Class: "card shadow-sm border-0 mx-auto", Style: "max-width: 32rem")[
                    Div(Class: "card-body")[
                        H1(Class: "h4 mb-3")["Edit __ENTITY__"],
                        Form(_form, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
        __FORMFIELDS__
                            Div(Class: "d-flex justify-content-end gap-2 pt-2")[
                                NavLink(Routes.__PLURAL__Page(), Class: "btn btn-outline-secondary")["Cancel"],
                                Button("submit", Class: "btn btn-primary")["Save changes"]
                            ]
                        ]
                    ]
                ];
            }
        }

        """;
}
