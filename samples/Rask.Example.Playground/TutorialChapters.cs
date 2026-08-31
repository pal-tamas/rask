namespace Rask.Example.Playground;

/// <summary>One chapter of the guided in-browser tutorial: prose, a goal, and the code it starts from.</summary>
/// <remarks>
///     Same contract as <see cref="PlaygroundSample"/> — the code is a raw string so what the reader sees is
///     exactly what compiles, defines a component named <c>Playground</c> as the entry point, lives in a
///     namespace (as a real Rask project does), and declares every component <c>partial</c>, which is what lets
///     the generator inject the builder entries that make a reader's own components chainable. The in-browser
///     compile has no MSBuild implicit usings, so every snippet spells out its <c>using</c>s.
///     <para>
///         Chapters with <see cref="TutorialChapter.NeedsDatabase"/> run real EF Core against a real SQLite
///         database inside the tab. Each one owns its own file (<c>ch5.db</c>, <c>ch6.db</c>, …) in the
///         browser's in-memory filesystem: chapters evolve the schema as they go, and
///         <c>EnsureCreated()</c> is a no-op against a database that already has tables, so sharing one file
///         across chapters would leave later chapters querying an older schema.
///     </para>
/// </remarks>
public sealed record TutorialChapter(
    string Id,
    int Number,
    string Title,
    string Goal,
    IReadOnlyList<string> Steps,
    string Code,
    bool NeedsDatabase = false);

/// <summary>
/// The guided track shown in the playground's Tutorial tab. It teaches the parts of Rask that can be learned
/// in a browser tab — components, state, composition, forms, then data with EF Core + SQLite — and hands off
/// to <c>docs/tutorial</c> for the parts that need a real machine (migrations, jobs, mail, deploy).
/// </summary>
public static class TutorialChapters
{
    public static readonly IReadOnlyList<TutorialChapter> All = new[]
    {
        new TutorialChapter(
            "component",
            1,
            "Your first component",
            "Render some HTML from C#.",
            [
                "A component is a class deriving from Component that overrides Render().",
                "Markup is a chain: name a component, then dot onto it — Div.Class(\"card\"). No new, no factory call.",
                "Children go in the [] indexer: Div[H1[\"Hi\"]]. A tag you set nothing on needs no parentheses.",
                "Declare components partial — that is where the generator puts the chain surface.",
                "Try it: add a third item to the list, then press Run.",
            ],
            """
            using Rask.Core;

            namespace Demo;

            // Welcome! This C# is compiled inside your browser — Roslyn and the Rask source generator run
            // in WebAssembly, with no server involved. Press Run (or Ctrl/Cmd + Enter) to see the result.
            // The entry point is always a component named `Playground`.
            //
            // Markup is a chain. `Div` is a div, so pressing `.` after it lists everything a div has —
            // the documentation is at the call site. Set nothing on a tag and you need no parentheses
            // at all: `H1["Rask Coffee"]`.
            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Rask Coffee ☕"],
                        P.Class("muted")["Everything below is rendered from the C# on the left."],
                        Ul.Class("list")[
                            Li["Espresso"],
                            Li["Flat white"]
                        ]
                    ];
            }
            """),

        new TutorialChapter(
            "state",
            2,
            "State and events",
            "Hold state in a field and change it from an event handler.",
            [
                "State is an ordinary C# field — no observables, no setState.",
                ".OnClick(…) takes a plain delegate; Rask re-renders the component when it returns.",
                "The framework diffs the result and patches only what changed.",
                "Try it: add a \"Reset\" button that sets _stock back to 0.",
            ],
            """
            using Rask.Core;

            namespace Demo;

            // State lives in fields. A handler mutates the field, and Rask re-renders — there is nothing
            // to subscribe to and nothing to notify.
            public sealed partial class Playground : Component
            {
                private int _stock;

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Espresso"],
                        P[$"{_stock} bags in stock."],
                        Div.Class("line")[
                            Button.Class("action").OnClick(() => _stock++)["Restock"],
                            Button.Class("action").OnClick(() => _stock--).Disabled(_stock == 0)["Sell"]
                        ]
                    ];
            }
            """),

        new TutorialChapter(
            "composition",
            3,
            "Composition and lists",
            "Extract a child component and render a keyed list of them.",
            [
                "Your own components join the chain too — ProductCard.Name(…), never `new`.",
                "A property that cannot be defaulted becomes a STEP the chain asks for first; the rest are optional.",
                ".Key(…) gives a list item a stable identity, so the diff moves rows instead of rewriting them.",
                "Try it: add a Price property to ProductCard and render it.",
            ],
            """
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Core;

            namespace Demo;

            // A child component. The generator gives it a chain entry of its own, built from its public
            // properties — that chain is how you build one (constructing components with `new` is a
            // compile error, RASK014). It has to be `partial` for the generator to have somewhere to put it.
            public sealed partial class ProductCard : Component
            {
                // A card with no name is not a card, so `Name` is a STEP: the chain will not hand you a
                // ProductCard until you have set it, which makes forgetting it a compile error here rather
                // than a blank row at runtime. `InStock` has a sensible default, so it is a plain setter.
                public required string Name { get; set; }
                public bool InStock { get; set; } = true;

                protected override Component? Render() =>
                    Li.Class(InStock ? "item" : "item muted")[
                        Span[Name],
                        Span.Class("muted")[InStock ? " — in stock" : " — sold out"]
                    ];
            }

            public sealed partial class Playground : Component
            {
                private readonly List<string> _sold = new() { "Flat white" };

                private static readonly string[] Menu = ["Espresso", "Flat white", "Cold brew"];

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Menu"],
                        Ul.Class("list")[
                            // .Key(…) is the row's identity across re-renders — always set it on a list.
                            // A chain that ends in [ … ] is already a Component; this one ends on a
                            // setter, so it is still a builder — hence the cast on a whole list of them.
                            Menu.Select(name => (Component)ProductCard
                                .Name(name)
                                .InStock(!_sold.Contains(name))
                                .Key(name))
                        ],
                        Button.Class("action").OnClick(() => _sold.Clear())["Restock everything"]
                    ];
            }
            """),

        new TutorialChapter(
            "forms",
            4,
            "Forms and validation",
            "Bind inputs to a model and validate them as the reader types.",
            [
                "Input.Bind(() => model.Field) is a two-way binding — no name strings, no event plumbing.",
                "A control opens with .Bind(…) or .Value(…): it cannot exist until it knows what it holds.",
                ".Validate(…) runs per field; Form's own .Validate(…) runs across the whole model.",
                "ValidationMessage.Template(…).For(…) renders one field's errors; ValidationSummary the form-level ones.",
                "Try it: require the price to be greater than zero.",
            ],
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Core;
            using Rask.Core.Forms;

            namespace Demo;

            // Form binds to a plain model object and re-validates as you type. Nothing here is
            // Rask-specific except the components — the model is an ordinary class.
            public sealed partial class Playground : Component
            {
                private readonly NewProduct _model = new();
                private readonly List<string> _added = new();

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Add a product"],
                        Form.Model(_model).OnValidSubmit(Add)[
                            Label["Name"],
                            // .Bind(…) opens the chain: an input has to know what it binds to before it
                            // is an input at all. The type comes with it — no Input<string>() anywhere.
                            Input.Bind(() => _model.Name)
                                .Class("field")
                                .Placeholder("Cold brew")
                                .Validate(v => v.Trim().Length > 0
                                    ? Array.Empty<string>()
                                    : new[] { "Name is required." }),
                            ValidationMessage.Template(Errors).For(() => _model.Name),

                            Label["Price"],
                            Input.Bind(() => _model.Price).Class("field"),

                            Button.Type("submit").Class("action")["Add"]
                        ],
                        _added.Count == 0
                            ? null
                            : Ul.Class("list")[_added.Select(name => Li.Key(name)[name])]
                    ];

                private void Add(NewProduct model)
                {
                    _added.Add($"{model.Name} — {model.Price:0.00}");
                    model.Name = "";
                    model.Price = 0m;
                }

                private static Component Errors(IReadOnlyList<string> messages) =>
                    [.. messages.Select((m, i) => P.Key(i).Class("error")[m])];

                private sealed class NewProduct
                {
                    public string Name { get; set; } = "";
                    public decimal Price { get; set; }
                }
            }
            """),

        new TutorialChapter(
            "entity",
            5,
            "Your first entity",
            "Define an entity and a DbContext, create the database, and insert a row.",
            [
                "This runs real EF Core against a real SQLite database, inside your browser tab.",
                "Product derives from Rask.Data's Entity<Guid> — it gets the Id and the CreatedAt/UpdatedAt stamps.",
                "AuditingInterceptor fills those stamps in on save, so the application never sets them by hand.",
                "EnsureCreated() creates the file and the tables on first run — no migrations needed here.",
                "Try it: add a bag or two, press Run again, and watch them survive (until you reload the page).",
            ],
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Rask.Core;
            using Rask.Data;

            namespace Demo;

            // An entity. Entity<Guid> (from Rask.Data) owns the identity and the audit stamps — the same
            // shape the tutorial in docs/tutorial builds a real feature slice around.
            public sealed class Product : Entity<Guid>
            {
                private Product() { }   // EF Core materialises rows through this.

                public string Name { get; private set; } = "";
                public decimal Price { get; private set; }

                public static Product Create(string name, decimal price) =>
                    new() { Id = Guid.NewGuid(), Name = name, Price = price };
            }

            public sealed class ShopDb : DbContext
            {
                public DbSet<Product> Products => Set<Product>();

                protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                    options
                        // In the browser the database is a file in an in-memory filesystem: it lives as
                        // long as the tab does. On a server this is a path on disk.
                        // Pooling is off because a chapter may recreate its database between runs, and a
                        // pooled connection would keep serving the old, deleted file. A real app leaves
                        // pooling on.
                        .UseSqlite("Data Source=ch5.db;Pooling=False")
                        // Stamps CreatedAt/UpdatedAt on every save.
                        .AddInterceptors(new AuditingInterceptor(TimeProvider.System));

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Product>().Property(p => p.Name).HasMaxLength(100);

                    // Applies Rask.Data's conventions to whatever is already in the model, so call it
                    // after your configurations, never before.
                    modelBuilder.ApplyRaskConventions();
                }
            }

            public sealed partial class Playground : Component
            {
                private IReadOnlyList<Product> _products = [];

                protected override async Task OnMountAsync() => await LoadAsync();

                private async Task LoadAsync()
                {
                    await using var db = new ShopDb();
                    await db.Database.EnsureCreatedAsync(CancellationToken);
                    _products = await db.Products.AsNoTracking()
                        .OrderBy(p => p.Name)
                        .ToListAsync(CancellationToken);
                }

                // An awaited handler re-renders when it completes — no StateHasChanged() anywhere.
                private async Task AddAsync()
                {
                    await using var db = new ShopDb();
                    db.Products.Add(Product.Create("Espresso", 8.50m));
                    await db.SaveChangesAsync(CancellationToken);
                    await LoadAsync();
                }

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Stock"],
                        Button.Class("action").OnClickAsync(AddAsync)["Add a bag of espresso"],
                        _products.Count == 0
                            ? P.Class("muted")["No rows yet — press the button."]
                            : Ul.Class("list")[
                                _products.Select(p => Li.Key(p.Id)[
                                    Span[$"{p.Name} — {p.Price:0.00}"],
                                    // CreatedAt was filled in by the interceptor, not by Create().
                                    Span.Class("muted")[$" (added {p.CreatedAt:HH:mm:ss})"]
                                ])
                            ],
                        P.Class("muted")[$"{_products.Count} row(s) in the products table."]
                    ];
            }
            """,
            NeedsDatabase: true),

        new TutorialChapter(
            "query",
            6,
            "Query and display",
            "Filter and sort with LINQ, and render the results as a keyed list.",
            [
                "DbSet<T> is IQueryable — the Where/OrderBy below become SQL, run by SQLite in the tab.",
                "AsNoTracking() skips the change tracker: cheaper, and right for read-only screens.",
                "This chapter reseeds its own database each run, so the results are always the same.",
                "Try it: sort by price descending instead, or filter on Price rather than Name.",
            ],
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Rask.Core;
            using Rask.Data;

            namespace Demo;

            public sealed class Product : Entity<Guid>
            {
                private Product() { }

                public string Name { get; private set; } = "";
                public decimal Price { get; private set; }

                public static Product Create(string name, decimal price) =>
                    new() { Id = Guid.NewGuid(), Name = name, Price = price };
            }

            public sealed class ShopDb : DbContext
            {
                public DbSet<Product> Products => Set<Product>();

                protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                    options.UseSqlite("Data Source=ch6.db;Pooling=False")
                           .AddInterceptors(new AuditingInterceptor(TimeProvider.System));

                protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                    modelBuilder.ApplyRaskConventions();
            }

            public sealed partial class Playground : Component
            {
                private readonly Search _search = new();
                private IReadOnlyList<Product> _results = [];

                protected override async Task OnMountAsync()
                {
                    await using var db = new ShopDb();

                    // A throwaway demo database: drop it and rebuild it so every run starts identical.
                    await db.Database.EnsureDeletedAsync(CancellationToken);
                    await db.Database.EnsureCreatedAsync(CancellationToken);
                    db.Products.AddRange(
                        Product.Create("Espresso", 8.50m),
                        Product.Create("Flat white", 9.00m),
                        Product.Create("Cold brew", 11.25m),
                        Product.Create("Decaf", 7.75m));
                    await db.SaveChangesAsync(CancellationToken);

                    await SearchAsync();
                }

                private async Task SearchAsync()
                {
                    await using var db = new ShopDb();

                    // Composed in C#, translated to SQL, executed by SQLite — in the browser.
                    var query = db.Products.AsNoTracking();
                    if (_search.Term.Trim().Length > 0)
                    {
                        query = query.Where(p => p.Name.Contains(_search.Term));
                    }

                    _results = await query.OrderBy(p => p.Name).ToListAsync(CancellationToken);
                }

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Catalogue"],
                        Div.Class("line")[
                            Input.Bind(() => _search.Term).Class("field").Placeholder("Search by name…"),
                            Button.Class("action").OnClickAsync(SearchAsync)["Search"]
                        ],
                        _results.Count == 0
                            ? P.Class("muted")["Nothing matched."]
                            : Ul.Class("list")[
                                _results.Select(p => Li.Key(p.Id)[$"{p.Name} — {p.Price:0.00}"])
                            ]
                    ];

                private sealed class Search
                {
                    public string Term { get; set; } = "";
                }
            }
            """,
            NeedsDatabase: true),

        new TutorialChapter(
            "edit-delete",
            7,
            "Edit and delete",
            "Update a row, then soft-delete one and watch the query filter hide it.",
            [
                "Load a tracked entity, change it, SaveChanges — EF works out the UPDATE for you.",
                "Implementing ISoftDeletable turns Remove() into a DeletedAt stamp instead of a DELETE.",
                "ApplyRaskConventions() adds a `DeletedAt == null` filter to every query for that entity.",
                "IgnoreQueryFilters() is how you see the deleted rows again — try the toggle below.",
            ],
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Rask.Core;
            using Rask.Data;

            namespace Demo;

            // ISoftDeletable opts this entity into soft delete. Nothing else changes.
            public sealed class Product : Entity<Guid>, ISoftDeletable
            {
                private Product() { }

                public string Name { get; private set; } = "";
                public decimal Price { get; private set; }
                public DateTime? DeletedAt { get; private set; }

                public static Product Create(string name, decimal price) =>
                    new() { Id = Guid.NewGuid(), Name = name, Price = price };

                public void Reprice(decimal price) => Price = price;
            }

            public sealed class ShopDb : DbContext
            {
                public DbSet<Product> Products => Set<Product>();

                protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                    options.UseSqlite("Data Source=ch7.db;Pooling=False")
                           .AddInterceptors(
                               // Order matters: the soft delete rewrites the entry to Modified, and the
                               // auditor then stamps UpdatedAt on it.
                               new SoftDeleteInterceptor(TimeProvider.System),
                               new AuditingInterceptor(TimeProvider.System));

                protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                    modelBuilder.ApplyRaskConventions();
            }

            public sealed partial class Playground : Component
            {
                private bool _showDeleted;
                private IReadOnlyList<Product> _products = [];

                protected override async Task OnMountAsync()
                {
                    await using var db = new ShopDb();
                    await db.Database.EnsureDeletedAsync(CancellationToken);
                    await db.Database.EnsureCreatedAsync(CancellationToken);
                    db.Products.AddRange(
                        Product.Create("Espresso", 8.50m),
                        Product.Create("Flat white", 9.00m),
                        Product.Create("Cold brew", 11.25m));
                    await db.SaveChangesAsync(CancellationToken);

                    await LoadAsync();
                }

                private async Task LoadAsync()
                {
                    await using var db = new ShopDb();
                    var query = db.Products.AsNoTracking();

                    // The filter is on by default — this is the only way past it.
                    if (_showDeleted)
                    {
                        query = query.IgnoreQueryFilters();
                    }

                    _products = await query.OrderBy(p => p.Name).ToListAsync(CancellationToken);
                }

                private async Task RepriceAsync(Guid id)
                {
                    await using var db = new ShopDb();
                    var product = await db.Products.FirstAsync(p => p.Id == id, CancellationToken);
                    product.Reprice(product.Price + 0.50m);
                    await db.SaveChangesAsync(CancellationToken);   // UPDATE, and UpdatedAt is restamped
                    await LoadAsync();
                }

                private async Task DeleteAsync(Guid id)
                {
                    await using var db = new ShopDb();
                    var product = await db.Products.FirstAsync(p => p.Id == id, CancellationToken);
                    db.Products.Remove(product);                    // becomes "set DeletedAt = now"
                    await db.SaveChangesAsync(CancellationToken);
                    await LoadAsync();
                }

                private async Task ToggleAsync()
                {
                    _showDeleted = !_showDeleted;
                    await LoadAsync();
                }

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Catalogue"],
                        Ul.Class("list")[
                            _products.Select(p => Li.Key(p.Id).Class(p.DeletedAt is null ? null : "done")[
                                Span[$"{p.Name} — {p.Price:0.00}"],
                                Button.Class("link").OnClickAsync(() => RepriceAsync(p.Id))["+50c"],
                                Button.Class("link").OnClickAsync(() => DeleteAsync(p.Id))["Delete"]
                            ])
                        ],
                        Button.Class("action").OnClickAsync(ToggleAsync)[
                            _showDeleted ? "Hide deleted" : "Show deleted"
                        ],
                        P.Class("muted")[
                            _showDeleted
                                ? "IgnoreQueryFilters() — deleted rows are still there, just stamped."
                                : "The default query filter hides anything with a DeletedAt."
                        ]
                    ];
            }
            """,
            NeedsDatabase: true),

        new TutorialChapter(
            "relationships",
            8,
            "Relationships",
            "Model an order with lines, save the whole graph, and read it back with Include.",
            [
                "A navigation property is the relationship — EF infers the foreign key from it.",
                "Adding the parent saves the children too: one SaveChanges, one transaction.",
                "Include() loads a navigation; without it the collection comes back empty.",
                "That is the whole browser tutorial — the next step is `rask new`, on your own machine.",
            ],
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Rask.Core;
            using Rask.Data;

            namespace Demo;

            public sealed class Order : Entity<Guid>
            {
                private readonly List<OrderLine> _lines = new();

                private Order() { }

                public string Customer { get; private set; } = "";

                // The navigation. EF fills the collection; callers only read it.
                public IReadOnlyCollection<OrderLine> Lines => _lines;

                public decimal Total => _lines.Sum(l => l.Price * l.Quantity);

                public static Order For(string customer) =>
                    new() { Id = Guid.NewGuid(), Customer = customer };

                public Order With(string product, decimal price, int quantity)
                {
                    _lines.Add(OrderLine.Create(product, price, quantity));
                    return this;
                }
            }

            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }

                public string Product { get; private set; } = "";
                public decimal Price { get; private set; }
                public int Quantity { get; private set; }

                public static OrderLine Create(string product, decimal price, int quantity) =>
                    new() { Id = Guid.NewGuid(), Product = product, Price = price, Quantity = quantity };
            }

            public sealed class ShopDb : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();

                protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                    options.UseSqlite("Data Source=ch8.db;Pooling=False")
                           .AddInterceptors(new AuditingInterceptor(TimeProvider.System));

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    // One order has many lines. EF adds the foreign key and the backing field access.
                    modelBuilder.Entity<Order>()
                        .HasMany(o => o.Lines)
                        .WithOne()
                        .OnDelete(DeleteBehavior.Cascade);
                    modelBuilder.Entity<Order>().Ignore(o => o.Total);

                    modelBuilder.ApplyRaskConventions();
                }
            }

            public sealed partial class Playground : Component
            {
                private IReadOnlyList<Order> _orders = [];

                protected override async Task OnMountAsync()
                {
                    await using var db = new ShopDb();
                    await db.Database.EnsureDeletedAsync(CancellationToken);
                    await db.Database.EnsureCreatedAsync(CancellationToken);

                    // Saving the order saves its lines with it, in one transaction.
                    db.Orders.Add(Order.For("Ada")
                        .With("Espresso", 8.50m, 2)
                        .With("Cold brew", 11.25m, 1));
                    db.Orders.Add(Order.For("Grace").With("Flat white", 9.00m, 3));
                    await db.SaveChangesAsync(CancellationToken);

                    _orders = await db.Orders.AsNoTracking()
                        .Include(o => o.Lines)          // drop this and the lines come back empty
                        .OrderBy(o => o.Customer)
                        .ToListAsync(CancellationToken);
                }

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Orders"],
                        // A list of children goes in an indexer of its own — an enumerable can't sit
                        // alongside single components in the same one.
                        Div[
                            _orders.Select(o => Div.Key(o.Id)[
                                H2[$"{o.Customer} — {o.Total:0.00}"],
                                Ul.Class("list")[
                                    o.Lines.Select(l => Li.Key(l.Id)[
                                        $"{l.Quantity} × {l.Product} @ {l.Price:0.00}"
                                    ])
                                ]
                            ])
                        ],
                        P.Class("muted")["Done! Run `rask new` on your machine for the rest of the story."]
                    ];
            }
            """,
            NeedsDatabase: true),
    };

    /// <summary>The chapter the Tutorial tab opens on.</summary>
    public static TutorialChapter First => All[0];
}
