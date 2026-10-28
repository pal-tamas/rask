using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data.Tests.Companies;
using Rask.Data.Tests.Fuel;

namespace Rask.Data.Tests;

/// <summary>
/// Rows keyed by several columns on an aggregate's form: a link table as the list of the other part's ids, saved as a
/// set, and a row with fields of its own as a list of rows matched by key — both in the aggregate's one save.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class CompositeFormTests : IAsyncLifetime
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-composite-form-{Guid.NewGuid():N}.db");
    private readonly SqlCommandLog _log = new();
    private ServiceProvider _app = null!;
    private int[] _teaors = [];

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<FuelContext>();
        services.AddDbContextFactory<FuelContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
            .AddInterceptors(_log));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var teaors = new List<int>();
        foreach (var code in new[] { "0111", "0112", "0113", "0114" })
        {
            teaors.Add((await Teaor.Create(new TeaorModel { Code = code }, cancellationToken: TestContext.Current.CancellationToken)).Id);
        }

        _teaors = [.. teaors];
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_company_created_with_activity_ids_links_each_one()
    {
        var model = new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0], _teaors[1]] };

        var company = await CreateAsync(Acme, model);

        Assert.Equal([_teaors[0], _teaors[1]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task An_id_added_to_the_list_inserts_one_link_and_nothing_else()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0]] });
        _log.Clear();

        await UpdateAsync(Acme, company.Id, m => m.ActivityIds = [_teaors[0], _teaors[2]]);

        var write = Assert.Single(Writes());
        Assert.StartsWith("INSERT INTO \"CompanyTeaors\"", write, StringComparison.Ordinal);
        Assert.Equal([_teaors[0], _teaors[2]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task An_id_taken_off_the_list_deletes_its_link_and_nothing_else()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0], _teaors[1]] });
        _log.Clear();

        await UpdateAsync(Acme, company.Id, m => m.ActivityIds = [_teaors[1]]);

        var write = Assert.Single(Writes());
        Assert.StartsWith("DELETE FROM \"CompanyTeaors\"", write, StringComparison.Ordinal);
        Assert.Equal([_teaors[1]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task Ids_added_and_taken_off_in_one_save_insert_and_delete_by_key_and_never_update()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0], _teaors[1]] });
        _log.Clear();

        await UpdateAsync(Acme, company.Id, m => m.ActivityIds = [_teaors[1], _teaors[2], _teaors[3]]);

        var writes = Writes();
        Assert.Equal(3, writes.Count);
        Assert.Equal(2, writes.Count(static w => w.StartsWith("INSERT INTO \"CompanyTeaors\"", StringComparison.Ordinal)));
        Assert.Single(writes, static w => w.StartsWith("DELETE FROM \"CompanyTeaors\"", StringComparison.Ordinal));
        Assert.Equal([_teaors[1], _teaors[2], _teaors[3]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task The_same_set_in_another_order_writes_nothing()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0], _teaors[1]] });
        _log.Clear();

        await UpdateAsync(Acme, company.Id, m => m.ActivityIds = [_teaors[1], _teaors[0]]);

        Assert.Empty(Writes());
        Assert.Equal([_teaors[0], _teaors[1]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task An_id_given_twice_is_one_link()
    {
        var model = new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0], _teaors[0], _teaors[1]] };

        var company = await CreateAsync(Acme, model);

        Assert.Equal([_teaors[0], _teaors[1]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task An_id_that_names_no_activity_is_refused_under_the_list_of_ids()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0]] });

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => UpdateAsync(Acme, company.Id, m => m.ActivityIds = [_teaors[0], 99_999]));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["ActivityIds"], failure.Fields);
        Assert.Equal(HeldRowReference.Message, failure.Message);
        Assert.Equal([_teaors[0]], await ActivitiesAsync(Acme, company.Id));
    }

    [Fact]
    public async Task A_refused_id_is_told_in_the_sentence_the_host_handed_over()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", ActivityIds = [_teaors[0]] });
        var english = HeldRowReference.Sentence;
        HeldRowReference.Sentence = static () => "Az itt kiválasztott elem nem létezik.";

        RaskValidationException refused;
        try
        {
            refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => UpdateAsync(Acme, company.Id, m => m.ActivityIds = [99_999]));
        }
        finally
        {
            HeldRowReference.Sentence = english;
        }

        Assert.Equal("Az itt kiválasztott elem nem létezik.", Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task Another_tenants_company_cannot_be_given_links_and_its_own_are_kept()
    {
        var theirs = await CreateAsync(Globex, new CompanyModel { Name = "Globex", ActivityIds = [_teaors[0]] });

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => UpdateAsync(Acme, theirs.Id, m => m.ActivityIds = [_teaors[1]]));

        await using var context = await OpenAsync();
        Assert.Null(context.Model.FindEntityType(typeof(CompanyActivity))!.FindProperty(Columns.TenantId));
        Assert.Equal([_teaors[0]], await ActivitiesAsync(Globex, theirs.Id));
        Assert.Empty(await ActivitiesAsync(Acme, theirs.Id));
    }

    [Fact]
    public async Task Rows_posted_on_the_form_are_inserted_updated_and_deleted_by_their_key()
    {
        var company = await CreateAsync(Acme, new CompanyModel
        {
            Name = "Acme",
            Columns = [Column(1, 0, true), Column(2, 1, false), Column(3, 2, false)],
        });
        _log.Clear();

        await UpdateAsync(Acme, company.Id, m => m.Columns = [Column(1, 0, true), Column(2, 5, false), Column(4, 3, true)]);

        var writes = Writes();
        Assert.Equal(3, writes.Count);
        Assert.Single(writes, static w => w.StartsWith("UPDATE \"CompanyColumn\" SET \"Order\"", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("INSERT INTO \"CompanyColumn\"", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("DELETE FROM \"CompanyColumn\"", StringComparison.Ordinal));
        Assert.Equal(["1:0:True", "2:5:False", "4:3:True"], await ColumnsAsync(Acme, company.Id));
    }

    [Fact]
    public async Task Two_rows_of_one_key_are_refused_under_the_list_before_anything_is_written()
    {
        var company = await CreateAsync(Acme, new CompanyModel { Name = "Acme", Columns = [Column(1, 0, true)] });
        _log.Clear();

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => UpdateAsync(Acme, company.Id, m => m.Columns = [Column(1, 0, true), Column(1, 4, false)]));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["Columns"], failure.Fields);
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task A_row_whose_key_is_not_filled_in_is_refused_under_its_own_field()
    {
        var model = new CompanyModel { Name = "Acme", Columns = [new CompanyColumnModel { Order = 1, Sum = true }] };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => CreateAsync(Acme, model));

        Assert.Contains(refused.Failures, static f => f.Fields.SequenceEqual(["Columns[0].ColumnId"]));
    }

    [Fact]
    public async Task The_edit_form_is_filled_with_the_ids_and_rows_held_and_saves_back_unchanged()
    {
        var company = await CreateAsync(Acme, new CompanyModel
        {
            Name = "Acme",
            ActivityIds = [_teaors[2], _teaors[0]],
            Columns = [Column(1, 0, true), Column(2, 1, false)],
        });
        _log.Clear();

        CompanyModel? model;
        using (Tenant.Use(Acme))
        {
            model = await Company.Model(company.Id, cancellationToken: TestContext.Current.CancellationToken);
            await Company.Update(company.Id, model!, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal([_teaors[0], _teaors[2]], model!.ActivityIds.Order());
        Assert.Equal(["1:0:True", "2:1:False"], model.Columns.OrderBy(static c => c.ColumnId).Select(static c => $"{c.ColumnId}:{c.Order}:{c.Sum}"));
        Assert.Empty(Writes());
    }

    [Fact]
    public async Task Two_companies_saved_from_their_forms_on_one_shared_context_are_one_save()
    {
        var first = await CreateAsync(Acme, new CompanyModel { Name = "First", ActivityIds = [_teaors[0]] });
        _log.Clear();

        int added;
        using (Tenant.Use(Acme))
        {
            await using var db = await OpenAsync();
            await Company.Update(first.Id, new CompanyModel { Name = "First", ActivityIds = [_teaors[1]] }, db: db,
                cancellationToken: TestContext.Current.CancellationToken);
            var second = await Company.Create(new CompanyModel { Name = "Second", ActivityIds = [_teaors[2]], Columns = [Column(1, 0, true)] }, db: db,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(Writes());
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            added = second.Id;
        }

        Assert.Equal([_teaors[1]], await ActivitiesAsync(Acme, first.Id));
        Assert.Equal([_teaors[2]], await ActivitiesAsync(Acme, added));
        Assert.Equal(["1:0:True"], await ColumnsAsync(Acme, added));
    }

    [Fact]
    public async Task A_refused_id_in_one_transaction_keeps_every_company_saved_in_it_as_it_was()
    {
        var first = await CreateAsync(Acme, new CompanyModel { Name = "First", ActivityIds = [_teaors[0]] });
        var second = await CreateAsync(Acme, new CompanyModel { Name = "Second", ActivityIds = [_teaors[1]] });

        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<RaskValidationException>(() => Db.Transaction(async () =>
            {
                await Company.Update(first.Id, new CompanyModel { Name = "First", ActivityIds = [_teaors[3]] },
                    cancellationToken: TestContext.Current.CancellationToken);
                await Company.Update(second.Id, new CompanyModel { Name = "Second", ActivityIds = [99_999] },
                    cancellationToken: TestContext.Current.CancellationToken);
            }, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(["ActivityIds"], Assert.Single(refused.Failures).Fields);
        Assert.Equal([_teaors[0]], await ActivitiesAsync(Acme, first.Id));
        Assert.Equal([_teaors[1]], await ActivitiesAsync(Acme, second.Id));
    }

    private static CompanyColumnModel Column(int columnId, int order, bool sum) =>
        new() { ColumnId = columnId, Order = order, Sum = sum };

    private static async Task<Company> CreateAsync(int tenant, CompanyModel model)
    {
        using (Tenant.Use(tenant))
        {
            return await Company.Create(model, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // The stored company's edit form, changed by `edit`, saved back.
    private static async Task UpdateAsync(int tenant, int id, Action<CompanyModel> edit)
    {
        using (Tenant.Use(tenant))
        {
            var model = await Company.Model(id, cancellationToken: TestContext.Current.CancellationToken)
                        ?? throw new KeyNotFoundException();
            edit(model);
            await Company.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private async Task<List<int>> ActivitiesAsync(int tenant, int companyId)
    {
        await using var context = await OpenAsync();
        using (Tenant.Use(tenant))
        {
            var rows = await context.Set<CompanyActivity>().AsNoTracking()
                .Where(a => a.CompanyId == companyId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. rows.Select(static a => a.TeaorId).Order()];
        }
    }

    private async Task<List<string>> ColumnsAsync(int tenant, int companyId)
    {
        await using var context = await OpenAsync();
        using (Tenant.Use(tenant))
        {
            var columns = await context.Set<CompanyColumn>().AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. columns.OrderBy(static c => c.ColumnId).Select(static c => $"{c.ColumnId}:{c.Order}:{c.Sum}")];
        }
    }

    // The INSERT, UPDATE and DELETE statements the saves since the last Clear sent, one per row.
    private List<string> Writes() =>
        [.. _log.Texts
            .SelectMany(static t => t.Split(';'))
            .Select(static s => s.Trim())
            .Where(static s => s.StartsWith("INSERT", StringComparison.Ordinal)
                               || s.StartsWith("UPDATE", StringComparison.Ordinal)
                               || s.StartsWith("DELETE", StringComparison.Ordinal))];

    private Task<FuelContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<FuelContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
