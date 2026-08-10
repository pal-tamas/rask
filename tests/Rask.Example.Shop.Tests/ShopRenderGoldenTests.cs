using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Data;
using Rask.Example.Shop.Features.Orders;
using Rask.Example.Shop.Features.Products;
using Rask.Example.Shop.Features.Shared;
using Rask.SQLite;
using Rask.Testing;
// One `Generated` per feature namespace, and `Rask.Bootstrap.Generated` is globally imported here too,
// so the bare name is ambiguous. Aliases rather than fully-qualified call sites: this file names 14
// components and the qualification would be most of each line.
using AuthGen = Rask.Example.Shop.Features.Auth.Generated;
using HomeGen = Rask.Example.Shop.Features.Home.Generated;
using OrdersGen = Rask.Example.Shop.Features.Orders.Generated;
using ProductsGen = Rask.Example.Shop.Features.Products.Generated;
using SharedGen = Rask.Example.Shop.Features.Shared.Generated;

namespace Rask.Example.Shop.Tests;

/// <summary>
///     Every page of the sample, rendered to HTML and compared against a committed transcript.
/// </summary>
/// <remarks>
///     <para>
///         Written for the builder-surface migration, and useful well beyond it. Rewriting
///         <c>Foo(A: x)[…]</c> as <c>Foo.A(x)[…]</c> is a change no test in this project could see: the
///         suite here covers persistence, the generated registries and the CLI's provenance, none of
///         which touch markup. "It still compiles" is not evidence that a component still renders what
///         it rendered, and neither is a spot assertion on one element — the two surfaces differ in how
///         a child is created (<c>GetOrCreate</c> either way, but props arrive through setters
///         afterwards rather than as factory arguments) and in when a prop is reset, so the failure to
///         look for is a stale or missing attribute, anywhere.
///     </para>
///     <para>
///         So the assertion is the whole document, byte for byte. Set <c>RASK_UPDATE_GOLDEN=1</c> to
///         rewrite the transcript from what the app currently renders, then read the diff — the diff is
///         the artifact, not the file.
///     </para>
/// </remarks>
public sealed partial class ShopRenderGoldenTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-shop-golden-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;
    private readonly Guid _productId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _deletedProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _orderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly CultureInfo _culture = CultureInfo.DefaultThreadCurrentCulture ?? CultureInfo.CurrentCulture;

    public ShopRenderGoldenTests()
    {
        // A price renders through the ambient culture (`12.5` here, `12,5` on a machine set to hu-HU), and
        // the async loads continue on pool threads that would not inherit a culture set on this one. So it
        // is the DEFAULT that is pinned, and restored in Dispose — a committed transcript that only matches
        // on its author's machine is worse than no transcript.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        // The app's own registrations, in the app's own order — the point is to render what the app
        // renders, so a divergence here would be a divergence in the evidence.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
        services.AddRaskOutbox<AppDbContext>();
        services.AddDbContextFactory<AppDbContext>((sp, o) => o
            .UseRaskSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddRaskJobs<AppDbContext>();
        services.AddRaskCache<AppDbContext>();
        services.AddScoped<PopularProducts>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var route = TestRoute.At("/");
        services.AddSingleton(route);
        services.AddSingleton(TestRoute.NavigatorFor(route));
        services.AddSingleton<IAuthSignIn, StubAuth>();
        services.AddSingleton<IUserProvider>(new StubUser("ada", "admin"));
        services.AddSingleton<Rask.Example.Shop.Features.Auth.ICredentialStore, StubCredentials>();

        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    // Fixed ids and fixed values: the transcript has to be reproducible, and Product.Create/Order.Create
    // stamp a fresh Guid and a timestamp. The ids are overwritten before the insert; CreatedAt/UpdatedAt
    // never reach the markup, so they are left alone.
    private void Seed(AppDbContext db)
    {
        var live = Product.Create("Espresso beans", 12.50m, inStock: true);
        SetId(live, _productId);
        var gone = Product.Create("Discontinued grinder", 99m, inStock: false);
        SetId(gone, _deletedProductId);
        var order = Order.Create("Ada Lovelace", 19.99m);
        SetId(order, _orderId);

        db.Products.AddRange(live, gone);
        db.Orders.Add(order);
        db.SaveChanges();

        db.Products.Remove(gone); // soft delete — ProductsPage's "Show deleted" branch needs one
        db.SaveChanges();
    }

    private static void SetId(object entity, Guid id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);

    [Fact]
    public async Task Every_page_renders_what_it_rendered_before()
    {
        var actual = new StringBuilder();

        Append(actual, "HomePage", Render(() => HomeGen.HomePage()));
        Append(actual, "ErrorPage", Render(() => SharedGen.ErrorPage()));
        Append(actual, "OrderConfirmation",
            Render(() => OrdersGen.OrderConfirmation(Customer: "Ada Lovelace", Total: 19.99m)));
        Append(actual, "LoginPage", Render(() => AuthGen.LoginPage()));
        Append(actual, "MembersPage", Render(() => AuthGen.MembersPage()));
        Append(actual, "CreateProduct", Render(() => ProductsGen.CreateProduct()));
        Append(actual, "CreateOrder", Render(() => OrdersGen.CreateOrder()));

        // The pages that load through OnMountAsync / OnPropsChangedAsync. ProductsPage is rendered twice
        // over: the placeholder is markup too, and it is the branch a factory-to-chain rewrite is most
        // likely to drop, since it sits in a different `return` from the one carrying all the props.
        Append(actual, "ProductsPage (loading)", Render(() => ProductsGen.ProductsPage()));
        Append(actual, "ProductsPage (loaded)",
            await RenderLoadedAsync(() => ProductsGen.ProductsPage(), "Espresso beans"));
        Append(actual, "OrdersPage (loaded)",
            await RenderLoadedAsync(() => OrdersGen.OrdersPage(), "Ada Lovelace"));
        Append(actual, "UpdateProduct (loaded)",
            await RenderLoadedAsync(() => ProductsGen.UpdateProduct(Id: _productId), "Edit Product"));
        Append(actual, "UpdateProduct (not found)",
            await RenderLoadedAsync(() => ProductsGen.UpdateProduct(Id: Guid.Empty), "not found"));
        Append(actual, "UpdateOrder (loaded)",
            await RenderLoadedAsync(() => OrdersGen.UpdateOrder(Id: _orderId), "Edit Order"));
        Append(actual, "UpdateOrder (not found)",
            await RenderLoadedAsync(() => OrdersGen.UpdateOrder(Id: Guid.Empty), "not found"));

        Append(actual, "OpsPage", RenderOps());

        // The document, not a component: App's Head override and the shell Rask composes around it.
        Append(actual, "App (document at /)", RaskTest.RenderDocument(SharedGen.App(), _provider).Html);

        var text = actual.ToString();
        var path = Path.Combine(AppContext.BaseDirectory, GoldenFile);

        if (Environment.GetEnvironmentVariable("RASK_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(SourcePath(), text);
        }

        Assert.True(File.Exists(path), $"{GoldenFile} is missing from the test output — is it still copied?");
        Assert.Equal(Normalize(File.ReadAllText(path)), Normalize(text));
    }

    // The transcript proves the markup, and markup is where `data-rask-on-submit="h0"` looks identical
    // whether the delegate behind it is the right one or a wrapper around nothing. That is precisely the
    // substitution the builder-surface migration makes at every scaffolded form: `Form`'s typed
    // `OnValidSubmitAsync` factory parameter has no setter of its own, so a chain has to reach the same
    // place through the untyped `OnValidSubmit` property. Byte-identical HTML would not notice. So: fill
    // the form, submit it, and look in the database.
    [Fact]
    public async Task Submitting_the_scaffolded_form_still_runs_its_command()
    {
        var page = RaskTest.Render(() => ProductsGen.CreateProduct(), _provider);
        await page.On("#name").InputAsync("Latte cups");
        await page.On("form").SubmitAsync();

        await using var db = await _provider.GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContextAsync();
        // Filtered after materialising: Name is a value object, so the comparison has no SQL translation.
        Assert.Single(await db.Products.ToListAsync(), p => p.Name.Value == "Latte cups");
    }

    private const string GoldenFile = "ShopRender.golden.txt";

    // The golden lives next to the source, and the build copies it to the output directory. Writing the
    // update back to the source (not the copy) is what makes RASK_UPDATE_GOLDEN produce a reviewable diff.
    private static string SourcePath() =>
        Path.Combine(
            Path.GetDirectoryName(typeof(ShopRenderGoldenTests).Assembly.Location)!,
            "..", "..", "..", GoldenFile);

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void Append(StringBuilder sb, string name, string html) =>
        sb.Append("==== ").Append(name).Append(" ====\n").Append(html).Append("\n\n");

    private string Render(Func<Rask.Core.Component?> factory) => RaskTest.Render(factory, _provider).Html;

    private async Task<string> RenderLoadedAsync(Func<Rask.Core.Component?> factory, string expected)
    {
        var page = RaskTest.Render(factory, _provider);
        return await page.WaitForAsync(expected);
    }

    // OpsPage starts a bounded poll loop in OnMountAsync and stops it in Dispose, so it is the one page
    // whose instance this test has to own: rendering it through its factory would leave a task querying a
    // database this class is about to delete. Its first render is asserted, which is every branch that
    // does not depend on the poll.
#pragma warning disable RASK014 // the documented per-file opt-out: a test that deliberately constructs one
    private string RenderOps()
    {
        using var page = new Rask.Example.Shop.Features.Ops.OpsPage(
            _provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            _provider.GetRequiredService<IJobQueue>(),
            _provider.GetRequiredService<PopularProducts>(),
            _provider.GetRequiredService<IConfiguration>());

        return RaskTest.Render(page, _provider).Html;
    }
#pragma warning restore RASK014

    public void Dispose()
    {
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        _provider.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private sealed class StubAuth : IAuthSignIn
    {
        public Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null) =>
            Task.CompletedTask;

        public Task SignOutAsync(string? returnUrl = null, string? scheme = null) => Task.CompletedTask;
    }

    private sealed class StubUser(string name, string role) : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, role)], "test"));

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class StubCredentials : Rask.Example.Shop.Features.Auth.ICredentialStore
    {
        public IReadOnlyList<Claim>? Validate(string username, string password) => null;
    }
}
