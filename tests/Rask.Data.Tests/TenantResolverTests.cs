using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>
/// <c>services.AddRaskTenant(sp =&gt; …)</c>: the application says where the tenant of a request or a live
/// session comes from, and every filter and stamp uses that — asked once per scope and kept.
/// </summary>
/// <remarks>
/// The rule most worth pinning is what happens when the resolver names NO tenant: a tenant-scoped table reads
/// as empty and refuses a write, and never returns every tenant's rows. Each of those tests has other
/// tenants' rows — and a row that belongs to nobody — in the table while it reads.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class TenantResolverTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenant-resolver-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_read_is_filtered_by_the_tenant_the_resolver_named()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => Acme);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
            Assert.Equal(TenantNumber.ToGuid(Acme), Current.Tenant);
        }
    }

    [Fact]
    public async Task A_save_is_stamped_with_the_tenant_the_resolver_named()
    {
        await using var database = await StartDatabaseAsync();
        await using var app = App(_ => Globex);
        var destination = Destination.Named("Graz");

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            await destination.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(Globex, destination.TenantId);
    }

    [Fact]
    public async Task Each_scope_is_asked_once_and_keeps_its_own_answer()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        var answers = new Queue<int?>([Acme, Globex]);
        var asked = 0;
        await using var app = App(_ =>
        {
            asked++;
            return answers.Dequeue();
        });

        await using var first = app.CreateAsyncScope();
        await using var second = app.CreateAsyncScope();
        using (Db.UseScope(first.ServiceProvider))
        {
            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
            Assert.Equal(1, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
        }

        using (Db.UseScope(second.ServiceProvider))
        {
            Assert.Equal(["Vienna"], await Destination.Select(d => d.Name));
        }

        // The first scope again, long after the second was asked: a live session keeps the answer it opened with.
        using (Db.UseScope(first.ServiceProvider))
        {
            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
        }

        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task The_resolver_is_handed_the_services_of_the_scope_in_flight()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(sp => sp.GetRequiredService<RequestStub>().TenantId, s => s.AddScoped<RequestStub>());

        await using var request = app.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<RequestStub>().TenantId = Globex;
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(["Vienna"], await Destination.Select(d => d.Name));
        }
    }

    [Fact]
    public async Task An_explicit_tenant_wins_over_the_resolver_and_the_resolver_is_not_asked()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        var asked = 0;
        await using var app = App(_ =>
        {
            asked++;
            return Acme;
        });

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        using (Tenant.Use(Globex))
        {
            Assert.Equal(["Vienna"], await Destination.Select(d => d.Name));
        }

        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Across_wins_over_the_resolver()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => Acme);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        using (Tenant.Across())
        {
            Assert.Equal(3, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task With_no_tenant_resolved_every_read_of_a_tenant_scoped_table_is_empty()
    {
        await using var database = await StartDatabaseAsync();
        var theirs = await SeedAsync(database);
        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(0, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Empty(await Destination.All);
            Assert.Empty(await Destination.Where(d => d.Name != "").Select(d => d.Name));
            Assert.Null(await Destination.Find(theirs, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(Current.Tenant);
        }
    }

    [Fact]
    public async Task With_no_tenant_resolved_a_table_whose_tenant_is_a_guid_is_empty_too()
    {
        await using var database = await StartDatabaseAsync();
        using (Tenant.Use(Guid.NewGuid()))
        {
            database.Context.Add(Ledger.For("ACME-1"));
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Empty(await Ledger.All);
            Assert.Empty(await database.Context.Set<Ledger>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task With_no_tenant_resolved_a_save_is_refused_and_nothing_is_written()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            var refused = await Assert.ThrowsAsync<MissingTenantException>(
                () => Destination.Named("Graz").Save(cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("AddRaskTenant", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(["Budapest", "Nowhere", "Vienna"], await StoredNamesAsync(database));
    }

    [Fact]
    public async Task With_no_tenant_resolved_an_update_and_a_delete_are_refused_by_name()
    {
        await using var database = await StartDatabaseAsync();
        var theirs = await SeedAsync(database);
        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            await Assert.ThrowsAsync<MissingTenantException>(() => Destination.Update(
                theirs, d => d.Rename("taken"), cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<MissingTenantException>(
                () => Destination.Delete(theirs, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(["Budapest", "Nowhere", "Vienna"], await StoredNamesAsync(database));
    }

    [Fact]
    public async Task With_no_tenant_resolved_a_row_loaded_earlier_cannot_be_saved_through_a_context()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        Destination tracked;
        using (Tenant.Use(Acme))
        {
            tracked = await database.Context.Set<Destination>().SingleAsync(TestContext.Current.CancellationToken);
        }

        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            tracked.Rename("taken");

            await Assert.ThrowsAsync<MissingTenantException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        database.Context.ChangeTracker.Clear();
        Assert.Equal(["Budapest", "Nowhere", "Vienna"], await StoredNamesAsync(database));
    }

    [Fact]
    public async Task With_no_tenant_resolved_a_filter_an_app_wrote_itself_is_handed_a_value_no_row_holds()
    {
        await using var database = await StartDatabaseAsync();
        await using var app = App(_ => null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            // Null here would mean "do not restrict" to a filter written as `current == null || …`.
            Assert.Equal(Guid.AllBitsSet, Tenant.Resolve());
            Assert.Equal(Guid.AllBitsSet, database.Context.CurrentTenant);
        }
    }

    [Fact]
    public void The_value_that_stands_for_no_tenant_cannot_be_opened_as_one()
    {
        var refused = Assert.Throws<ArgumentException>(() => Tenant.Use(Guid.AllBitsSet));

        Assert.Equal("tenant", refused.ParamName);
    }

    [Fact]
    public async Task Without_a_resolver_the_signed_in_users_claim_still_names_the_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(resolve: null, s => s.AddScoped<IPrincipalSource>(_ => new ClaimSource(Globex)));

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(["Vienna"], await Destination.Select(d => d.Name));
        }
    }

    [Fact]
    public async Task Without_a_resolver_a_read_with_no_tenant_still_throws_rather_than_reading_empty()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(resolve: null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Destination.All);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Destination.Named("Graz").Save(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task With_a_resolver_a_user_with_no_tenant_claim_works_in_the_resolved_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => Acme, s => s.AddScoped<IPrincipalSource>(_ => new ClaimSource(tenant: null)));

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
        }
    }

    [Fact]
    public async Task With_a_resolver_a_user_whose_claim_names_another_tenant_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => Acme, s => s.AddScoped<IPrincipalSource>(_ => new ClaimSource(Globex)));

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            await Assert.ThrowsAsync<ForbiddenException>(async () => await Destination.All);
            await Assert.ThrowsAsync<ForbiddenException>(
                () => Destination.Named("Graz").Save(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task With_a_resolver_that_names_no_tenant_the_users_claim_does_not_stand_in_for_it()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        await using var app = App(_ => null, s => s.AddScoped<IPrincipalSource>(_ => new ClaimSource(Globex)));

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Empty(await Destination.All);
        }
    }

    [Fact]
    public async Task A_resolver_can_name_a_tenant_by_its_guid()
    {
        await using var database = await StartDatabaseAsync();
        var acme = Guid.NewGuid();
        foreach (var (tenant, reference) in new[] { (acme, "ACME-1"), (Guid.NewGuid(), "GLOBEX-1") })
        {
            using (Tenant.Use(tenant))
            {
                database.Context.Add(Ledger.For(reference));
                await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        await using var app = new ServiceCollection().AddRaskTenant(_ => acme).BuildServiceProvider(validateScopes: true);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(["ACME-1"], await Ledger.Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task A_resolver_that_throws_is_asked_again_rather_than_remembered_as_no_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        var asked = 0;
        await using var app = App(_ => asked++ == 0 ? throw new TimeoutException("the tenant lookup timed out") : Acme);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            await Assert.ThrowsAsync<TimeoutException>(async () => await Destination.All);

            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
        }
    }

    [Fact]
    public async Task A_resolver_that_reads_the_tenant_it_is_asked_for_is_told_so()
    {
        await using var database = await StartDatabaseAsync();
        await using var app = App(_ => TenantNumber.TryRead(Current.Tenant ?? Guid.Empty, out var number) ? (int)number : null);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Destination.All);

            Assert.Contains("AddRaskTenant", refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_host_opening_a_scope_asks_the_resolver_before_anything_reads_the_tenant()
    {
        var asked = 0;
        await using var app = App(_ =>
        {
            asked++;
            return Acme;
        });

        await using var request = app.CreateAsyncScope();
        using (Db.OpenScope(request.ServiceProvider))
        using (Db.OpenScope(request.ServiceProvider))
        {
            Assert.Equal(1, asked);
        }

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task The_host_opening_a_scope_asks_nothing_in_an_app_with_no_resolver()
    {
        await using var app = App(resolve: null);

        await using var request = app.CreateAsyncScope();
        using (Db.OpenScope(request.ServiceProvider))
        {
            Assert.Null(ResolvedTenant.For(request.ServiceProvider));
        }
    }

    [Fact]
    public void A_second_resolver_is_refused_at_registration()
    {
        var services = new ServiceCollection().AddRaskTenant(_ => Acme);

        var refused = Assert.Throws<InvalidOperationException>(() => services.AddRaskTenant(_ => Guid.NewGuid()));

        Assert.Contains("already registered", refused.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider App(Func<IServiceProvider, int?>? resolve, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);

        if (resolve is not null)
        {
            // The shape an app writes: an int? returned as it is, picked up by the numbered overload.
            services.AddRaskTenant(sp => resolve(sp));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    // Budapest for Acme, Vienna for Globex, and a row that belongs to no tenant at all. Returns Budapest's key.
    private static async Task<int> SeedAsync(TestDatabase database)
    {
        var budapest = Destination.Named("Budapest");
        using (Tenant.Use(Acme))
        {
            await budapest.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            await Destination.Named("Vienna").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Destinations (Name, TenantId) VALUES ('Nowhere', NULL)", TestContext.Current.CancellationToken);

        return budapest.Id;
    }

    // Straight to the table, past every filter: what the database holds, whoever is asking.
    private static async Task<List<string>> StoredNamesAsync(TestDatabase database) =>
        await database.Context.Database
            .SqlQuery<string>($"SELECT Name AS Value FROM Destinations ORDER BY Name")
            .ToListAsync(TestContext.Current.CancellationToken);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));

    private sealed class RequestStub
    {
        public int? TenantId { get; set; }
    }

    private sealed class ClaimSource(int? tenant) : IPrincipalSource
    {
        public ClaimsPrincipal? Current { get; } = new(new ClaimsIdentity(
            tenant is { } number ? [new Claim(Tenant.ClaimType, TenantNumber.ToGuid(number).ToString())] : [],
            "Test"));
    }
}
