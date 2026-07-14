using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a full CRUD vertical slice under <c>Features/&lt;Plural&gt;/</c>: a POCO entity, a DbContext
/// (unless an existing one is named with <c>--context</c>), and list / create / edit pages wired to EF Core
/// through <c>IDbContextFactory</c>. The output compiles as-is; a printed next-steps note covers the one
/// DI registration and the migration needed to make it run.
/// </summary>
internal static class FeatureGenerator
{
    public static ScaffoldResult Generate(
        ProjectContext project,
        string baseDirectory,
        string entityName,
        IReadOnlyList<FieldSpec> fields,
        string? contextOverride,
        string? pluralOverride,
        string? outputOverride)
    {
        var plural = pluralOverride ?? Pluralizer.Pluralize(entityName);
        var route = Identifiers.ToRoutePath(plural);
        var generateContext = contextOverride is null;
        var context = contextOverride ?? plural + "DbContext";

        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Features", plural);
        var @namespace = project.NamespaceFor(targetDirectory);

        var files = new List<ScaffoldFile>
        {
            new(Path.Combine(targetDirectory, entityName + ".cs"), RenderEntity(@namespace, entityName, fields)),
        };

        if (generateContext)
        {
            files.Add(new(Path.Combine(targetDirectory, context + ".cs"), RenderDbContext(@namespace, context, entityName, plural)));
        }

        files.Add(new(Path.Combine(targetDirectory, plural + "Page.cs"), RenderListPage(@namespace, entityName, plural, context, route, fields)));
        files.Add(new(Path.Combine(targetDirectory, "Create" + entityName + "Page.cs"), RenderCreatePage(@namespace, entityName, plural, context, route, fields)));
        files.Add(new(Path.Combine(targetDirectory, "Edit" + entityName + "Page.cs"), RenderEditPage(@namespace, entityName, plural, context, route, fields)));

        return new ScaffoldResult(files, RenderNextSteps(context, entityName, plural, route, generateContext));
    }

    internal static string RenderEntity(string @namespace, string entity, IReadOnlyList<FieldSpec> fields)
    {
        var properties = new StringBuilder();
        properties.Append("    public int Id { get; set; }").Append('\n');
        foreach (var field in fields)
        {
            properties.Append("    public ").Append(field.CsType).Append(' ').Append(field.Name).Append(" { get; set; }");
            if (field.Initializer is not null)
            {
                // Only an initialized auto-property takes a trailing ';' (e.g. string = "";).
                properties.Append(' ').Append(field.Initializer).Append(';');
            }

            properties.Append('\n');
        }

        return $$"""
        namespace {{@namespace}};

        public sealed class {{entity}}
        {
        {{properties.ToString().TrimEnd('\n')}}
        }

        """;
    }

    internal static string RenderDbContext(string @namespace, string context, string entity, string plural) =>
        $$"""
        using Microsoft.EntityFrameworkCore;

        namespace {{@namespace}};

        public sealed class {{context}}(DbContextOptions<{{context}}> options) : DbContext(options)
        {
            public DbSet<{{entity}}> {{plural}} => Set<{{entity}}>();
        }

        """;

    private static string RenderListPage(string @namespace, string entity, string plural, string context, string route, IReadOnlyList<FieldSpec> fields)
    {
        var headers = new StringBuilder();
        var cells = new StringBuilder();
        foreach (var field in fields)
        {
            headers.Append("                            Th()[\"").Append(field.Name).Append("\"],\n");
            var cell = field.CsType == "string"
                ? "Td()[x." + field.Name + "]"
                : "Td()[$\"{x." + field.Name + "}\"]";
            cells.Append("                            ").Append(cell).Append(",\n");
        }

        return Apply(
            """
            using System.Globalization;
            using Microsoft.EntityFrameworkCore;
            using Rask.Core.Routing;

            namespace __NS__;

            [Route("__ROUTE__")]
            public sealed class __PLURAL__Page(IDbContextFactory<__CONTEXT__> dbContextFactory) : Component
            {
                private IReadOnlyList<__ENTITY__> _items = [];
                private bool _loaded;

                protected override Component? Head => Title()["__PLURAL__"];

                protected override async Task OnMountAsync() => await LoadAsync();

                private async Task LoadAsync()
                {
                    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                    _items = await db.__PLURAL__.AsNoTracking().OrderBy(x => x.Id).ToListAsync(CancellationToken);
                    _loaded = true;
                }

                private async Task DeleteAsync(int id)
                {
                    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                    await db.__PLURAL__.Where(x => x.Id == id).ExecuteDeleteAsync(CancellationToken);
                    await LoadAsync();
                }

                protected override Component? Render() =>
                [
                    Div(Class: "d-flex justify-content-between align-items-center mb-3")[
                        H1(Class: "h3 mb-0")["__PLURAL__"],
                        NavLink(Routes.Create__ENTITY__Page(), Class: "btn btn-primary")["New __ENTITY__"]
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
                                        Td()[x.Id.ToString(CultureInfo.InvariantCulture)],
            __CELLS__
                                        Td(Class: "text-end text-nowrap")[
                                            NavLink(Routes.Edit__ENTITY__Page(x.Id), Class: "btn btn-outline-secondary btn-sm me-1")["Edit"],
                                            Button("button", Class: "btn btn-outline-danger btn-sm", OnClickAsync: () => DeleteAsync(x.Id))["Delete"]
                                        ]
                                    ])
                                ]
                            ]
                ];
            }

            """,
            ("__NS__", @namespace), ("__ENTITY__", entity), ("__PLURAL__", plural), ("__CONTEXT__", context), ("__ROUTE__", route),
            ("__HEADERS__", headers.ToString().TrimEnd('\n')), ("__CELLS__", cells.ToString().TrimEnd('\n')));
    }

    private static string RenderCreatePage(string @namespace, string entity, string plural, string context, string route, IReadOnlyList<FieldSpec> fields) =>
        Apply(
            """
            using Microsoft.EntityFrameworkCore;
            using Rask.Core.Routing;

            namespace __NS__;

            [Route("__ROUTE__/new")]
            public sealed class Create__ENTITY__Page(IDbContextFactory<__CONTEXT__> dbContextFactory, Navigator navigator) : Component
            {
                private readonly __ENTITY__ _item = new();

                protected override Component? Head => Title()["New __ENTITY__"];

                private async Task SubmitAsync(__ENTITY__ item)
                {
                    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                    db.__PLURAL__.Add(item);
                    await db.SaveChangesAsync(CancellationToken);
                    navigator.NavigateTo(Routes.__PLURAL__Page());
                }

                protected override Component? Render() =>
                    Div(Class: "card shadow-sm border-0 mx-auto", Style: "max-width: 32rem")[
                        Div(Class: "card-body")[
                            H1(Class: "h4 mb-3")["New __ENTITY__"],
                            Form(_item, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
            __FIELDS__
                                Div(Class: "d-flex justify-content-end gap-2 pt-2")[
                                    NavLink(Routes.__PLURAL__Page(), Class: "btn btn-outline-secondary")["Cancel"],
                                    Button("submit", Class: "btn btn-primary")["Save"]
                                ]
                            ]
                        ]
                    ];
            }

            """,
            ("__NS__", @namespace), ("__ENTITY__", entity), ("__PLURAL__", plural), ("__CONTEXT__", context), ("__ROUTE__", route),
            ("__FIELDS__", RenderFormFields(fields, "_item")));

    private static string RenderEditPage(string @namespace, string entity, string plural, string context, string route, IReadOnlyList<FieldSpec> fields) =>
        Apply(
            """
            using Microsoft.EntityFrameworkCore;
            using Rask.Core.Routing;

            namespace __NS__;

            [Route("__ROUTE__/{id:int}/edit")]
            public sealed class Edit__ENTITY__Page(IDbContextFactory<__CONTEXT__> dbContextFactory, Navigator navigator) : Component
            {
                private __ENTITY__ _item = new();
                private bool _loaded;
                private bool _found;

                [RouteParam] public int Id { get; set; }

                protected override Component? Head => Title()["Edit __ENTITY__"];

                protected override async Task OnPropsChangedAsync()
                {
                    _loaded = false;
                    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                    var item = await db.__PLURAL__.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id, CancellationToken);
                    _found = item is not null;
                    if (item is not null)
                    {
                        _item = item;
                    }

                    _loaded = true;
                }

                private async Task SubmitAsync(__ENTITY__ item)
                {
                    await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
                    db.__PLURAL__.Update(item);
                    await db.SaveChangesAsync(CancellationToken);
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
                            Form(_item, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
            __FIELDS__
                                Div(Class: "d-flex justify-content-end gap-2 pt-2")[
                                    NavLink(Routes.__PLURAL__Page(), Class: "btn btn-outline-secondary")["Cancel"],
                                    Button("submit", Class: "btn btn-primary")["Save changes"]
                                ]
                            ]
                        ]
                    ];
                }
            }

            """,
            ("__NS__", @namespace), ("__ENTITY__", entity), ("__PLURAL__", plural), ("__CONTEXT__", context), ("__ROUTE__", route),
            ("__FIELDS__", RenderFormFields(fields, "_item")));

    private static string RenderFormFields(IReadOnlyList<FieldSpec> fields, string model)
    {
        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            var id = field.Name.ToLowerInvariant();
            if (field.CsType == "bool")
            {
                // A bound bool renders as a checkbox, which Bootstrap styles with form-check /
                // form-check-input / form-check-label (not the text-input form-control).
                builder
                    .Append("                    Div(Class: \"form-check\")[\n")
                    .Append("                        Input(() => ").Append(model).Append('.').Append(field.Name).Append(", Id: \"").Append(id).Append("\", Class: \"form-check-input\"),\n")
                    .Append("                        Label(\"").Append(id).Append("\", Class: \"form-check-label\")[\"").Append(field.Name).Append("\"]\n")
                    .Append("                    ],\n");
            }
            else
            {
                builder
                    .Append("                    Div()[\n")
                    .Append("                        Label(\"").Append(id).Append("\", Class: \"form-label small mb-1\")[\"").Append(field.Name).Append("\"],\n")
                    .Append("                        Input(() => ").Append(model).Append('.').Append(field.Name).Append(", Id: \"").Append(id).Append("\", Class: \"form-control\")\n")
                    .Append("                    ],\n");
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string RenderNextSteps(string context, string entity, string plural, string route, bool generatedContext)
    {
        var steps = new StringBuilder();
        steps.Append("Next steps:\n");
        steps.Append("  1. Reference EF Core + SQLite if the project doesn't already:\n");
        steps.Append("       dotnet add package Microsoft.EntityFrameworkCore.Sqlite\n");

        if (generatedContext)
        {
            steps.Append("  2. Register the data context in Program.cs:\n");
            steps.Append("       builder.Services.AddDbContextFactory<").Append(context).Append(">(o => o.UseSqlite(\"Data Source=app.db\"));\n");
        }
        else
        {
            steps.Append("  2. Add the entity to your ").Append(context).Append(":\n");
            steps.Append("       public DbSet<").Append(entity).Append("> ").Append(plural).Append(" => Set<").Append(entity).Append(">();\n");
        }

        steps.Append("  3. Create the schema (EF Core migrations):\n");
        steps.Append("       dotnet add package Microsoft.EntityFrameworkCore.Design\n");
        steps.Append("       dotnet ef migrations add Add").Append(entity).Append(" && dotnet ef database update\n");
        steps.Append("  4. Run the app and browse to ").Append(route).Append('.');
        return steps.ToString();
    }

    private static string Apply(string template, params (string Token, string Value)[] replacements)
    {
        foreach (var (token, value) in replacements)
        {
            template = template.Replace(token, value, StringComparison.Ordinal);
        }

        return template;
    }
}
