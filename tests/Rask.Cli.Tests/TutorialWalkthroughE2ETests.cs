using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The docs walk-through, compiled. <c>docs/tutorial/</c> takes a reader from <c>rask new Shop</c> to a
/// deployed, database-backed product across nine chapters — and it's the framework's flagship proof, so its
/// code must never rot. This gate reproduces chapters 1-8 exactly as a reader would: it runs the real
/// generators the CLI runs (<see cref="ProjectGenerator.GenerateServer"/>, <see cref="FeatureGenerator"/>,
/// <see cref="JobGenerator"/>, <see cref="EmailGenerator"/>), applies the CLI's real Program.cs / DbContext
/// splices (<see cref="GenerateCommand.SpliceProgramCs"/>, <see cref="GenerateCommand.SpliceContext"/>), and
/// then applies the <b>hand-written code the prose tells the reader to type</b> — the job body, the
/// <c>IMailQueue</c> send, the <c>ICache.GetOrCreateAsync</c> read-through, the <c>IOutboxEvent</c> +
/// <c>INotificationHandler</c>, the <c>Entity.Raise</c>, the Litestream wiring — and builds the whole thing
/// with <c>-warnaserror</c>.
/// </summary>
/// <remarks>
/// <para>
/// The point is to catch tutorial drift the moment it happens: if a Rask package changes a signature the
/// tutorial uses (<c>Email.To(...).Body(component)</c>, <c>ICache.GetOrCreateAsync</c>, <c>Entity.Raise</c>,
/// <c>AddRaskJobs&lt;T&gt;</c>'s options, <c>AddRaskSqliteLitestream</c>), this build breaks in the same commit
/// rather than in a beginner's terminal one release later. <see cref="CliBuildE2E.Diagnostics"/> folds the C#
/// errors into the failure so each maps back to the chapter whose snippet stopped compiling.
/// </para>
/// <para>
/// <b>Boundaries.</b> This is a compile gate, not a run: it does <b>not</b> run <c>rask db add</c>/<c>update</c>
/// (real <c>dotnet-ef</c> + a database — a network + state side effect the suite deliberately avoids), nor
/// chapter 9's <c>rask deploy</c> (SSH/Docker, covered by <c>DeployCommandTests</c>/<c>HostBootstrapTests</c>).
/// Building the fully-wired app proves every <c>AddRask*</c> registration, every <c>modelBuilder.AddRask*</c>
/// map, and every prose API resolves against this commit's packages — the tutorial's compile contract.
/// </para>
/// <para>
/// Where the prose edits generated <i>handlers</i> in place (chapter 4's enqueue, chapter 6's cache rewrite),
/// this test compiles the exact prose bodies against the real generated <c>ProductsDbContext</c>/<c>Product</c>
/// types in dedicated companion files rather than string-surgering the generated templates — same API
/// coverage, without a false failure every time an unrelated template detail is reformatted. Edits that have a
/// single stable anchor (the wiring splices, <c>Order.Create</c>'s one-liner, the wholly hand-authored job/
/// email files) are applied faithfully in place and assert their anchor, so a genuine drift still fails loudly.
/// </para>
/// </remarks>
public sealed class TutorialWalkthroughE2ETests
{
    [Fact]
    public async Task Tutorial_shop_walkthrough_builds()
    {
        if (!CliBuildE2E.Enabled)
        {
            return; // opt-in: this packs the repo + restores + builds. Set RASK_CLI_BUILD_E2E=1.
        }

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string Name = "Shop";
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, Name);
        try
        {
            var fs = new SystemFileSystem();
            var project = new ProjectContext(projectDir, Name);
            var packages = new HashSet<string>(StringComparer.Ordinal);

            var productsDir = Path.Combine(projectDir, "Features", "Products");
            var ordersDir = Path.Combine(projectDir, "Features", "Orders");
            var sharedDir = Path.Combine(projectDir, "Features", "Shared");
            var productsCtx = Path.Combine(productsDir, "ProductsDbContext.cs");
            var programPath = Path.Combine(projectDir, "Program.cs");
            var productsNs = project.NamespaceFor(productsDir);   // Shop.Features.Products

            // --- local helpers (mirror what GenerateCommand does after each command) ---
            // Writes a scaffold's files and records the NuGet packages it needs (`dotnet add package` is the
            // command's job, not the generator's). The server template's own refs (Rask.Server/Rask.Bootstrap)
            // are already baked into its csproj, so its ScaffoldResult carries none to add here.
            void WriteFiles(ScaffoldResult r)
            {
                foreach (var file in r.Files)
                {
                    fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                    fs.WriteAllText(file.Path, file.Content);
                }

                packages.UnionWith(r.Packages);
            }

            // The real Program.cs splice (idempotent, exactly as WireProgramCs runs it).
            void Program(IReadOnlyList<string> usings, IReadOnlyList<string> registrations)
            {
                var (updated, _) = GenerateCommand.SpliceProgramCs(fs.ReadAllText(programPath), usings, registrations);
                fs.WriteAllText(programPath, updated);
            }

            // The real DbContext splice (DbSets + OnModelCreating maps), exactly as EditContext runs it.
            void Context(IReadOnlyList<string> usings, IReadOnlyList<string> dbSets, IReadOnlyList<string> modelLines)
            {
                var (updated, _, _, _) = GenerateCommand.SpliceContext(fs.ReadAllText(productsCtx), usings, dbSets, modelLines);
                fs.WriteAllText(productsCtx, updated);
            }

            // A targeted in-place edit for the few prose changes that replace a generated line. Asserts the
            // anchor is present so a genuine template drift fails loudly instead of silently no-op'ing.
            void Replace(string path, string anchor, string replacement)
            {
                var text = fs.ReadAllText(path);
                Assert.True(text.Contains(anchor, StringComparison.Ordinal),
                    $"tutorial edit anchor not found in {Path.GetFileName(path)}: {anchor}");
                fs.WriteAllText(path, text.Replace(anchor, replacement, StringComparison.Ordinal));
            }

            // ===== Chapter 1 — rask new Shop --auth --docker =====
            // The server template's own package refs (Rask.Server/Rask.Bootstrap) are already in its csproj, so
            // the CLI never `dotnet add`s them — subtract them before injecting so restore sees no duplicates.
            var host = ProjectGenerator.GenerateServer(projectDir, Name, auth: true, pwa: false, cqrs: false, data: false, docker: true, version);
            WriteFiles(host);

            // ===== Chapter 2 — rask generate feature Product ... --validation dataannotations =====
            var product = new EntitySpec("Product", "Products",
            [
                new FieldSpec("Name", "string", IsNullable: false, MaxLength: 200),
                new FieldSpec("Price", "decimal", IsNullable: false, MaxLength: null),
                new FieldSpec("InStock", "bool", IsNullable: false, MaxLength: null),
            ]);
            var f2 = FeatureGenerator.Generate(project, projectDir, new FeatureSpec(product, []),
                new FeatureOptions { IdType = "Guid", Validation = "dataannotations" });
            WriteFiles(f2);
            Program(f2.ProgramUsings, f2.ProgramRegistrations);

            // ===== Chapter 3 — rask generate feature Order ... --context ProductsDbContext =====
            var order = new EntitySpec("Order", "Orders",
            [
                new FieldSpec("Total", "decimal", IsNullable: false, MaxLength: null),
                new FieldSpec("ProductId", "Guid", IsNullable: false, MaxLength: null),
                new FieldSpec("Placed", "DateTime", IsNullable: false, MaxLength: null),
            ]);
            // GenerateCommand resolves ContextNamespace/ContextFilePath by scanning the project; here we know them
            // (the context we just wrote), so supply them the way TryBuild does — ContextFilePath lands on the result.
            var f3 = FeatureGenerator.Generate(project, projectDir, new FeatureSpec(order, []),
                new FeatureOptions
                {
                    IdType = "Guid",
                    Validation = "dataannotations",
                    ContextOverride = "ProductsDbContext",
                    ContextNamespace = productsNs,
                }) with
            { ContextFilePath = productsCtx };
            WriteFiles(f3);
            Program(f3.ProgramUsings, f3.ProgramRegistrations);
            Context(f3.ContextUsings, f3.ContextDbSets, f3.ContextModelLines);   // adds DbSet<Order> to ProductsDbContext

            // ===== Chapter 4 — rask generate job SendOrderReceipt =====
            var f4 = JobGenerator.Generate(project, projectDir, "SendOrderReceipt", feature: null, outputOverride: null);
            WriteFiles(f4);
            // Wire jobs (manual, as the prose shows): registration + the jobs table.
            Program(["Rask.Jobs", productsNs],
            [
                """
                builder.Services.AddRaskJobs<ProductsDbContext>(o =>
                {
                    o.PollInterval = TimeSpan.FromSeconds(5);
                    o.MaxAttempts = 25;
                });
                """,
            ]);
            Context(["Rask.Jobs"], [], ["        modelBuilder.AddRaskJobs();"]);
            // The prose enqueues from CreateOrderCommandHandler; compiled here so IJobQueue.EnqueueAsync is gated.
            fs.WriteAllText(Path.Combine(sharedDir, "OrderEnqueuer.cs"), OrderEnqueuerProse);

            // ===== Chapter 5 — rask generate email OrderReceipt (auto-wires) =====
            var f5 = EmailGenerator.Generate(project, projectDir, "OrderReceipt", feature: null, outputOverride: null,
                context: ("ProductsDbContext", productsNs, productsCtx));
            WriteFiles(f5);
            Program(f5.ProgramUsings, f5.ProgramRegistrations);         // AddRaskMail<ProductsDbContext>
            Context([], [], f5.ContextModelLines);                      // modelBuilder.AddRaskMail()

            // The prose fills in the generated stubs: the OrderReceipt body (ch5) and the job handler that reads
            // the order and sends the receipt (ch4 + ch5). These files are hand-authored in the tutorial, so we
            // write the finished versions verbatim.
            fs.WriteAllText(Path.Combine(sharedDir, "OrderReceipt.cs"), OrderReceiptComponent);
            fs.WriteAllText(Path.Combine(sharedDir, "SendOrderReceipt.cs"), SendOrderReceiptJob);

            // ===== Chapter 6 — caching the catalog =====
            Program(["Rask.Cache", productsNs], ["builder.Services.AddRaskCache<ProductsDbContext>();"]);
            Context(["Rask.Cache"], [], ["        modelBuilder.AddRaskCache();"]);
            packages.Add("Rask.Cache");
            fs.WriteAllText(Path.Combine(sharedDir, "CatalogCache.cs"), CatalogCacheProse);

            // ===== Chapter 7 — domain events + the outbox =====
            fs.WriteAllText(Path.Combine(ordersDir, "OrderEvents.cs"), OrderEvents);
            fs.WriteAllText(Path.Combine(ordersDir, "OrderPlacedHandler.cs"), OrderPlacedHandler);
            // Raise the event from Order.Create (the generated one-liner becomes a body that raises).
            Replace(Path.Combine(ordersDir, "Order.cs"),
                "public static Order Create(decimal total, Guid productId, DateTime placed) => new(total, productId, placed);",
                """
                public static Order Create(decimal total, Guid productId, DateTime placed)
                    {
                        var order = new Order(total, productId, placed);
                        order.Raise(new OrderPlaced(order.Id));
                        return order;
                    }
                """);
            // Hand domain events to the outbox (post-commit) instead of dispatching them in-process.
            Replace(programPath,
                "builder.Services.AddRaskData();",
                "builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);");
            Program(["Rask.Outbox", productsNs],
            [
                """
                builder.Services.AddRaskOutbox<ProductsDbContext>(o =>
                {
                    o.PollInterval = TimeSpan.FromSeconds(5);
                    o.MaxAttempts = 10;
                });
                """,
            ]);
            Context(["Rask.Outbox"], [], ["        modelBuilder.AddRaskOutbox();"]);
            packages.Add("Rask.Outbox");

            // ===== Chapter 8 — production SQLite + Litestream =====
            // (Ch8 step 1's UseRaskSqlite swap is already what the generator emits — no edit needed.)
            Program(["Rask.SQLite.Litestream"],
            [
                """
                builder.Services.AddRaskSqliteLitestream(o =>
                {
                    o.DatabasePath = "app.db";
                    o.ReplicaUrl = "s3://my-bucket/shop";
                });
                """,
            ]);
            Replace(programPath, "app.Run();",
                """
                await app.Services.RestoreSqliteFromLitestreamAsync();

                app.Run();
                """);
            packages.Add("Rask.SQLite.Litestream");

            // ===== Build the fully-wired Shop =====
            packages.ExceptWith(host.Packages);   // already baked into the template csproj
            CliBuildE2E.InjectPackages(fs, Path.Combine(projectDir, Name + ".csproj"), packages.ToList(), version);
            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, Name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"tutorial Shop walk-through failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    // ---- Verbatim prose from the tutorial chapters, compiled against the generated Shop types ----

    // docs/tutorial/05-email.md — the finished email body. Data rides public init properties (so the generated
    // factory can pass it); Component + the HTML factories are global usings.
    private const string OrderReceiptComponent =
        """
        namespace Shop.Features.Shared;

        public sealed class OrderReceipt : Component
        {
            public Guid OrderId { get; set; }
            public decimal Total { get; set; }

            protected override Component? Render() =>
                Div()[
                    H1()["Thanks for your order!"],
                    P()[$"Order {OrderId} — total ", Strong()[$"{Total:C}"], "."],
                    P()["We'll email again when it ships."]
                ];
        }
        """;

    // docs/tutorial/04-background-jobs.md + 05-email.md — the finished job handler: read the order, send the receipt.
    private const string SendOrderReceiptJob =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Cqrs;
        using Rask.Jobs;
        using Rask.Mail;
        using Shop.Features.Orders;
        using Shop.Features.Products;

        namespace Shop.Features.Shared;

        public sealed record SendOrderReceipt(Guid OrderId) : IJob;

        public sealed class SendOrderReceiptHandler(
            IDbContextFactory<ProductsDbContext> dbFactory,
            IMailQueue mail) : ICommandHandler<SendOrderReceipt>
        {
            public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var order = await db.Orders.FindAsync([job.OrderId], ct);
                if (order is null)
                {
                    return;
                }

                await mail.SendAsync(
                    Email.To("customer@example.com")
                         .Subject($"Your order {order.Id}")
                         .Body(OrderReceipt(OrderId: order.Id, Total: order.Total)),   // the generated factory, not new
                    ct);
            }
        }
        """;

    // docs/tutorial/04-background-jobs.md — enqueue the job right after the order is saved (IJobQueue.EnqueueAsync).
    private const string OrderEnqueuerProse =
        """
        using Rask.Jobs;

        namespace Shop.Features.Shared;

        public sealed class OrderEnqueuer(IJobQueue jobs)
        {
            public Task EnqueueAsync(Guid orderId, CancellationToken cancellationToken) =>
                jobs.EnqueueAsync(new SendOrderReceipt(orderId), cancellationToken);
        }
        """;

    // docs/tutorial/06-cache.md — the ProductListItem projection + GetOrCreateAsync read-through + RemoveAsync
    // invalidation, compiled against the real ProductsDbContext/Product. Named distinctly from the generated
    // ListProductsQuery it mirrors so both coexist in the one assembly.
    private const string CatalogCacheProse =
        """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Caching.Distributed;
        using Rask.Cache;
        using Rask.Cqrs;
        using Shop.Features.Products;

        namespace Shop.Features.Shared;

        public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock);

        public sealed record CatalogQuery : IQuery<IReadOnlyList<ProductListItem>>;

        public sealed class CatalogQueryHandler(
            IDbContextFactory<ProductsDbContext> dbContextFactory,
            ICache cache) : IQueryHandler<CatalogQuery, IReadOnlyList<ProductListItem>>
        {
            public Task<IReadOnlyList<ProductListItem>> HandleAsync(CatalogQuery query, CancellationToken ct) =>
                cache.GetOrCreateAsync(
                    "catalog:all",
                    async token =>
                    {
                        await using var db = await dbContextFactory.CreateDbContextAsync(token);
                        return (IReadOnlyList<ProductListItem>)await db.Products
                            .AsNoTracking()
                            .OrderBy(p => p.Id)
                            .Select(p => new ProductListItem(p.Id, p.Name, p.Price, p.InStock))
                            .ToListAsync(token);
                    },
                    new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
        }

        public sealed class CatalogInvalidator(ICache cache)
        {
            public Task ClearAsync(CancellationToken ct) => cache.RemoveAsync("catalog:all", ct);
        }
        """;

    // docs/tutorial/07-outbox-events.md — the domain event and its handler.
    private const string OrderEvents =
        """
        using Rask.Outbox;

        namespace Shop.Features.Orders;

        public sealed record OrderPlaced(Guid Id) : IOutboxEvent;
        """;

    private const string OrderPlacedHandler =
        """
        using Microsoft.Extensions.Logging;
        using Rask.Cqrs;

        namespace Shop.Features.Orders;

        public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
            : INotificationHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken)
            {
                logger.LogInformation("Order {Id} placed — updating stock / analytics…", notification.Id);
                return Task.CompletedTask;
            }
        }
        """;
}
