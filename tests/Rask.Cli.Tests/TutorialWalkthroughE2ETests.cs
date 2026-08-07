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
/// this test compiles the exact prose bodies against the real generated <c>AppDbContext</c>/<c>Product</c>
/// types in dedicated companion files rather than string-surgering the generated templates — same API
/// coverage, without a false failure every time an unrelated template detail is reformatted. Edits that have a
/// single stable anchor (the wiring splices, <c>Order.Create</c>'s one-liner, the wholly hand-authored job/
/// email files) are applied faithfully in place and assert their anchor, so a genuine drift still fails loudly.
/// </para>
/// </remarks>
public sealed class TutorialWalkthroughE2ETests
{
    [SkippableFact]
    public async Task Tutorial_shop_walkthrough_builds()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

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
            // The context now comes from `rask new --data` (implied by --all-batteries), so it lives in the
            // Shared bucket rather than being created by the first `generate feature`.
            var appCtx = Path.Combine(sharedDir, "AppDbContext.cs");
            var programPath = Path.Combine(projectDir, "Program.cs");
            var sharedNs = project.NamespaceFor(sharedDir);       // Shop.Features.Shared

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
                var (updated, _, _, _) = GenerateCommand.SpliceContext(fs.ReadAllText(appCtx), usings, dbSets, modelLines);
                fs.WriteAllText(appCtx, updated);
            }

            // ===== Chapter 1 — rask new Shop --all-batteries --auth --docker =====
            // The server template's own package refs (Rask.Server/Rask.Bootstrap) are already in its csproj, so
            // the CLI never `dotnet add`s them — subtract them before injecting so restore sees no duplicates.
            var host = ProjectGenerator.GenerateServer(
                projectDir, Name, NewCommand.ToBatteries(["all-batteries", "auth", "docker"]), version);
            WriteFiles(host);

            // ===== Chapter 2 — rask generate feature Product ... =====
            var product = new EntitySpec("Product", "Products",
            [
                new FieldSpec("Name", "string", IsNullable: false, MaxLength: 200),
                new FieldSpec("Price", "decimal", IsNullable: false, MaxLength: null),
                new FieldSpec("InStock", "bool", IsNullable: false, MaxLength: null),
            ]);
            var f2 = FeatureGenerator.Generate(project, projectDir, new FeatureSpec(product, []),
                new FeatureOptions
                {
                    IdType = "Guid",
                    Validation = "dataannotations",
                    ExistingContext = "AppDbContext",
                    ContextNamespace = sharedNs,
                }) with
            { ContextFilePath = appCtx };
            WriteFiles(f2);
            Program(f2.ProgramUsings, f2.ProgramRegistrations);
            Context(f2.ContextUsings, f2.ContextDbSets, f2.ContextModelLines);

            // ===== Chapter 3 — rask generate feature Order ... =====
            var order = new EntitySpec("Order", "Orders",
            [
                new FieldSpec("Total", "decimal", IsNullable: false, MaxLength: null),
                new FieldSpec("ProductId", "Guid", IsNullable: false, MaxLength: null),
                new FieldSpec("Placed", "DateTime", IsNullable: false, MaxLength: null),
            ]);
            // GenerateCommand finds the app's one DbContext by scanning the project and attaches the slice to
            // it; here we know it already (Chapter 1 wrote it), so supply it the way TryBuild does.
            var f3 = FeatureGenerator.Generate(project, projectDir, new FeatureSpec(order, []),
                new FeatureOptions
                {
                    IdType = "Guid",
                    Validation = "dataannotations",
                    ExistingContext = "AppDbContext",
                    ContextNamespace = sharedNs,
                }) with
            { ContextFilePath = appCtx };
            WriteFiles(f3);
            Program(f3.ProgramUsings, f3.ProgramRegistrations);
            Context(f3.ContextUsings, f3.ContextDbSets, f3.ContextModelLines);   // adds DbSet<Order> to AppDbContext

            // ===== Chapter 4 — rask generate job SendOrderReceipt =====
            var f4 = JobGenerator.Generate(project, projectDir, "SendOrderReceipt", feature: null, outputOverride: null);
            WriteFiles(f4);
            // No wiring step: --all-batteries registered AddRaskJobs<AppDbContext>() and mapped the jobs
            // table in Chapter 1. Re-running the splice here would be a no-op, which is the point.
            // The prose enqueues from CreateOrderCommandHandler; compiled here so IJobQueue.EnqueueAsync is gated.
            fs.WriteAllText(Path.Combine(sharedDir, "OrderEnqueuer.cs"), OrderEnqueuerProse);

            // ===== Chapter 5 — rask generate email OrderReceipt (auto-wires) =====
            var f5 = EmailGenerator.Generate(project, projectDir, "OrderReceipt", feature: null, outputOverride: null,
                context: ("AppDbContext", sharedNs, appCtx));
            WriteFiles(f5);
            // Idempotent: mail is already registered, so these splices find it and leave it alone.
            Program(f5.ProgramUsings, f5.ProgramRegistrations);
            Context([], [], f5.ContextModelLines);

            // The prose fills in the generated stubs: the OrderReceipt body (ch5) and the job handler that reads
            // the order and sends the receipt (ch4 + ch5). These files are hand-authored in the tutorial, so we
            // write the finished versions verbatim.
            fs.WriteAllText(Path.Combine(sharedDir, "OrderReceipt.cs"), OrderReceiptComponent);
            fs.WriteAllText(Path.Combine(sharedDir, "SendOrderReceipt.cs"), SendOrderReceiptJob);

            // ===== Chapter 6 — rask generate cache CatalogCache --feature Products =====
            var f6 = CacheGenerator.Generate(project, projectDir, "CatalogCache", feature: "Products", outputOverride: null);
            WriteFiles(f6);
            // The prose replaces the generated stub with the real read-through over the catalog.
            fs.WriteAllText(Path.Combine(productsDir, "CatalogCache.cs"), CatalogCacheProse);

            // ===== Chapter 7 — rask generate feature Order ... --outbox --force =====
            // The chapter regenerates the Orders slice with events. That emits OrderEvents.cs (the
            // IOutboxEvent records), the Raise(...) calls on the entity, and OrderCreatedHandler — all the
            // pieces the prose used to tell you to write by hand.
            var f7 = FeatureGenerator.Generate(project, projectDir, new FeatureSpec(order, []),
                new FeatureOptions
                {
                    IdType = "Guid",
                    Validation = "dataannotations",
                    ExistingContext = "AppDbContext",
                    ContextNamespace = sharedNs,
                    UseOutbox = true,
                }) with
            { ContextFilePath = appCtx };
            WriteFiles(f7);
            Program(f7.ProgramUsings, f7.ProgramRegistrations);
            Context(f7.ContextUsings, f7.ContextDbSets, f7.ContextModelLines);

            // The one line the chapter is really about: with the outbox on, the in-process domain-event
            // publisher must be off. --all-batteries already wrote it that way, so assert it rather than
            // patching it — if that ever regresses, the outbox silently stops being durable.
            Assert.Contains(
                "o.DispatchDomainEventsInProcess = false;",
                fs.ReadAllText(programPath),
                StringComparison.Ordinal);

            // ===== Chapter 8 — production SQLite =====
            // Nothing to add: UseRaskSqlite, the snapshot service and the config-gated Litestream block
            // (plus its restore-before-anything-opens-the-database call) all came from --all-batteries.
            var program8 = fs.ReadAllText(programPath);
            Assert.Contains("UseRaskSqlite(", program8, StringComparison.Ordinal);
            Assert.Contains("AddRaskSqliteSnapshots(", program8, StringComparison.Ordinal);
            Assert.Contains("RestoreSqliteFromLitestreamAsync();", program8, StringComparison.Ordinal);

            // ===== Chapter 9 — push notifications =====
            // The endpoints and the subscription store are scaffolded; the prose adds a sender that reacts
            // to an order event. Compile it so IWebPushSender / WebPushMessage / WebPushStatus are gated.
            Assert.Contains("AddRaskWebPush(", program8, StringComparison.Ordinal);
            fs.WriteAllText(Path.Combine(ordersDir, "OrderShippedNotifier.cs"), OrderShippedNotifierProse);

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
        using Shop.Features.Shared;

        namespace Shop.Features.Shared;

        public sealed record SendOrderReceipt(Guid OrderId) : IJob;

        public sealed class SendOrderReceiptHandler(
            IDbContextFactory<AppDbContext> dbFactory,
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
    // invalidation, compiled against the real AppDbContext/Product. Named distinctly from the generated
    // ListProductsQuery it mirrors so both coexist in the one assembly.
    private const string CatalogCacheProse =
        """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Caching.Distributed;
        using Rask.Cache;
        using Rask.Cqrs;
        using Shop.Features.Products;
        using Shop.Features.Shared;

        namespace Shop.Features.Shared;

        public sealed record ProductListItem(Guid Id, string Name, decimal Price, bool InStock);

        public sealed record CatalogQuery : IQuery<IReadOnlyList<ProductListItem>>;

        public sealed class CatalogQueryHandler(
            IDbContextFactory<AppDbContext> dbContextFactory,
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

    // docs/tutorial/09-web-push.md — the sender that reacts to an order event. The event records, the
    // handler shape and the subscription store are all scaffolded; this is the body the prose adds.
    private const string OrderShippedNotifierProse =
        """
        using Rask.Cqrs;
        using Rask.WebPush;
        using Shop.Features.Push;

        namespace Shop.Features.Orders;

        public sealed class OrderShippedNotifier(IWebPushSender sender, PushSubscriptionStore store)
            : INotificationHandler<OrderCreated>
        {
            public async Task HandleAsync(OrderCreated notification, CancellationToken cancellationToken)
            {
                var message = WebPushMessage.Text(
                    "Your order shipped",
                    $"Order {notification.Id} is on its way.",
                    url: $"/orders/{notification.Id}");

                foreach (var subscription in store.All)
                {
                    var result = await sender.SendAsync(subscription, message, cancellationToken);

                    // A subscription that has expired (404/410) will never work again — drop it rather than
                    // retrying forever. ShouldDelete maps the status to that decision for you.
                    if (result.ShouldDelete)
                    {
                        store.Remove(subscription.Endpoint);
                    }
                }
            }
        }
        """;
}
