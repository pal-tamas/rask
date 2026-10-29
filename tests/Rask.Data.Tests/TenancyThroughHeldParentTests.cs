using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data.Tests;

/// <summary>A tenant's operating company — the aggregate the children below only point at.</summary>
public sealed class Outfit : Aggregate<string>, IFormModel, IPerTenant
{
    private Outfit() { }

    public static Outfit Named(string id) => new() { Id = id };
}

/// <summary>A tenant's storage tank, holding its cells.</summary>
public sealed class Cistern : Aggregate<string>, IFormModel, ISoftDeletable, IPerTenant
{
    private readonly List<Cell> _cells = [];

    private Cistern() { }

    public IReadOnlyList<Cell> Cells => _cells;

    public static Cistern Built(string id, params (string Cell, string Outfit)[] cells)
    {
        var cistern = new Cistern { Id = id };
        cistern._cells.AddRange(cells.Select(c => Cell.Of(c.Cell, c.Outfit)));
        return cistern;
    }
}

/// <summary>A cistern's cell, which names the outfit it is booked to.</summary>
public sealed class Cell : Entity<string>
{
    private Cell() { }

    public string OutfitId { get; private set; } = "";

    internal static Cell Of(string id, string outfit) => new() { Id = id, OutfitId = outfit };
}

/// <summary>A tenant's pump, holding its spouts.</summary>
public sealed class Bowser : Aggregate<string>, IFormModel, IPerTenant
{
    private readonly List<Spout> _spouts = [];

    private Bowser() { }

    public IReadOnlyList<Spout> Spouts => _spouts;

    public static Bowser Built(string id, string spout, string cistern)
    {
        var bowser = new Bowser { Id = id };
        bowser._spouts.Add(Spout.Of(spout, cistern));
        return bowser;
    }
}

/// <summary>A bowser's spout, which names the cistern it draws from.</summary>
public sealed class Spout : Entity<string>
{
    private Spout() { }

    public string CisternId { get; private set; } = "";

    internal static Spout Of(string id, string cistern) => new() { Id = id, CisternId = cistern };
}

/// <summary>A tenant's delivery, holding its lines.</summary>
public sealed class Dropoff : Aggregate<string>, IFormModel, IPerTenant
{
    private readonly List<DropoffLine> _lines = [];

    private Dropoff() { }

    public IReadOnlyList<DropoffLine> Lines => _lines;

    public static Dropoff Built(string id, string line, string cell)
    {
        var dropoff = new Dropoff { Id = id };
        dropoff._lines.Add(DropoffLine.Of(line, cell));
        return dropoff;
    }
}

/// <summary>A dropoff's line, which names the cell — another aggregate's child — it filled.</summary>
public sealed class DropoffLine : Entity<string>
{
    private DropoffLine() { }

    public string CellId { get; private set; } = "";

    internal static DropoffLine Of(string id, string cell) => new() { Id = id, CellId = cell };
}

/// <summary>One parent of a fob.</summary>
public sealed class Chauffeur : Aggregate<string>, IFormModel, IPerTenant
{
    private readonly List<Fob> _fobs = [];

    private Chauffeur() { }

    public IReadOnlyList<Fob> Fobs => _fobs;

    public static Chauffeur Built(string id, string fob, string outfit)
    {
        var chauffeur = new Chauffeur { Id = id };
        chauffeur._fobs.Add(Fob.Of(fob, outfit));
        return chauffeur;
    }
}

/// <summary>The other parent of a fob.</summary>
public sealed class Lorry : Aggregate<string>, IFormModel, IPerTenant
{
    private readonly List<Fob> _fobs = [];

    private Lorry() { }

    public IReadOnlyList<Fob> Fobs => _fobs;

    public static Lorry Built(string id, string fob, string outfit)
    {
        var lorry = new Lorry { Id = id };
        lorry._fobs.Add(Fob.Of(fob, outfit));
        return lorry;
    }
}

/// <summary>A child of two roots that also names the outfit it was issued by.</summary>
public sealed class Fob : Entity<string>
{
    private Fob() { }

    public string? ChauffeurId { get; private set; }

    public string? LorryId { get; private set; }

    public string OutfitId { get; private set; } = "";

    internal static Fob Of(string id, string outfit) => new() { Id = id, OutfitId = outfit };
}

/// <summary>
/// A child belongs to the tenant of the aggregate that HOLDS it, never to the tenant of an aggregate it merely names by
/// id — even though the read face gives that aggregate a collection of the rows that point at it.
/// </summary>
/// <remarks>
/// Every child below is held under Acme and points at something of Globex's (and the reverse), so a filter that
/// counted a pointed-at aggregate as a parent shows the row to both tenants and fails the test.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class TenancyThroughHeldParentTests : IDisposable
{
    private static readonly Guid Acme = new("6b1e0c4a-93d2-4f5e-8a71-0c2d9e4b3f18");
    private static readonly Guid Globex = new("d84a2f61-5c0b-4e9a-b3d7-91e6f2a0c5b4");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-held-parent-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_child_pointing_at_another_tenants_aggregate_is_read_by_its_roots_tenant_and_never_by_the_other()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        List<string> acme, globex;
        using (Tenant.Use(Acme))
        {
            acme = await Cell.Select(c => c.Id);
        }

        using (Tenant.Use(Globex))
        {
            globex = await Cell.Select(c => c.Id);
        }

        Assert.Equal(["c1"], acme);
        Assert.Equal(["c2"], globex);
    }

    [Fact]
    public async Task Each_child_is_read_only_by_the_tenant_of_the_root_that_holds_it()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        (List<string> Spouts, List<string> Lines, List<string> Fobs) acme, globex;
        using (Tenant.Use(Acme))
        {
            acme = (await Spout.Select(s => s.Id), await DropoffLine.Select(l => l.Id), await Fob.Select(f => f.Id));
        }

        using (Tenant.Use(Globex))
        {
            globex = (await Spout.Select(s => s.Id), await DropoffLine.Select(l => l.Id), await Fob.Select(f => f.Id));
        }

        Assert.Equal(["s1"], acme.Spouts);
        Assert.Equal(["l1"], acme.Lines);
        Assert.Equal(["f1", "f2"], acme.Fobs.Order(StringComparer.Ordinal));
        Assert.Empty(globex.Spouts);
        Assert.Empty(globex.Lines);
        Assert.Empty(globex.Fobs);
    }

    [Fact]
    public async Task The_read_filter_of_each_child_asks_only_the_roots_that_hold_it()
    {
        await using var database = await StartDatabaseAsync();
        await using var read = database.OpenRead();

        string cell, spout, line, fob;
        using (Tenant.Use(Acme))
        {
            cell = Where(read.Set<CellRead>().ToQueryString());
            spout = Where(read.Set<SpoutRead>().ToQueryString());
            line = Where(read.Set<DropoffLineRead>().ToQueryString());
            fob = Where(read.Set<FobRead>().ToQueryString());
        }

        Assert.Equal([TableOf(read, typeof(CisternRead))], TablesAsked(read, cell));
        Assert.Equal([TableOf(read, typeof(BowserRead))], TablesAsked(read, spout));
        Assert.Equal([TableOf(read, typeof(DropoffRead))], TablesAsked(read, line));
        Assert.Equal(
            new[] { TableOf(read, typeof(ChauffeurRead)), TableOf(read, typeof(LorryRead)) }.Order(StringComparer.Ordinal),
            TablesAsked(read, fob));
    }

    [Fact]
    public async Task A_child_whose_root_is_soft_deleted_is_hidden_though_the_aggregate_it_points_at_is_live()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        using (Tenant.Use(Acme))
        {
            await Outfit.Named("oa").Save(cancellationToken: token);
            await Cistern.Built("gone", ("c3", "oa")).Save(cancellationToken: token);
        }

        // Past the aggregate, which takes its children with it: a root marked deleted with its rows still there.
        var table = database.Context.Model.FindEntityType(typeof(Cistern))!.GetTableName();
        var markDeleted = $"UPDATE \"{table}\" SET \"{Columns.DeletedAt}\" = '2026-01-01 00:00:00+00:00' WHERE \"Id\" = 'gone'";
        await database.Context.Database.ExecuteSqlRawAsync(markDeleted, token);

        List<string> cells;
        using (Tenant.Use(Acme))
        {
            cells = await Cell.Select(c => c.Id);
        }

        Assert.Empty(cells);
    }

    [Fact]
    public async Task Every_read_face_asks_the_same_parents_as_its_write_entity_whatever_reverse_collections_it_gained()
    {
        await using var database = await StartDatabaseAsync();
        await using var read = database.OpenRead();

        var writeModel = database.Context.Model;
        var faces = read.Model.GetEntityTypes().Where(TenantParents.IsThroughParent).ToList();
        var reverse = read.Model.FindEntityType(typeof(CellRead))!.GetForeignKeys()
            .Where(static key => key.PrincipalToDependent is { IsCollection: true })
            .Select(static key => key.PrincipalEntityType.ClrType);

        Assert.Equal([typeof(CisternRead), typeof(OutfitRead)], reverse.OrderBy(static t => t.Name, StringComparer.Ordinal));
        Assert.Contains(read.Model.FindEntityType(typeof(FobRead))!, faces);
        Assert.All(faces, face => Assert.Equal(
            TenantParents.KeysOf(WriteEntityOf(writeModel, face)).Select(static k => k.PrincipalEntityType.ClrType.Name + "Read").Order(StringComparer.Ordinal),
            ParentsAsked(face).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task The_write_side_checks_and_severs_a_child_only_through_the_roots_that_hold_it()
    {
        await using var database = await StartDatabaseAsync();

        var model = database.Context.Model;
        string[] Parents(Type child) =>
            [.. TenantParents.KeysOf(model.FindEntityType(child)!).Select(static k => k.PrincipalEntityType.ClrType.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["Cistern"], Parents(typeof(Cell)));
        Assert.Equal(["Bowser"], Parents(typeof(Spout)));
        Assert.Equal(["Dropoff"], Parents(typeof(DropoffLine)));
        Assert.Equal(["Chauffeur", "Lorry"], Parents(typeof(Fob)));
        Assert.Equal(1, ModelBuilderExtensions.ParentKeysOf(model.FindEntityType(typeof(Cell))!));
        Assert.Equal(2, ModelBuilderExtensions.ParentKeysOf(model.FindEntityType(typeof(Fob))!));
    }

    // Acme holds every child, each pointing at Globex's: cell c1 -> outfit ob, spout s1 -> cistern cb, line l1 ->
    // cell c2, fobs f1 and f2 -> outfit ob. Globex holds cell c2, pointing back at Acme's outfit oa.
    private static async Task SeedAsync()
    {
        var token = TestContext.Current.CancellationToken;
        using (Tenant.Use(Acme))
        {
            await Outfit.Named("oa").Save(cancellationToken: token);
            await Cistern.Built("ca", ("c1", "ob")).Save(cancellationToken: token);
            await Bowser.Built("ba", "s1", "cb").Save(cancellationToken: token);
            await Dropoff.Built("da", "l1", "c2").Save(cancellationToken: token);
            await Chauffeur.Built("ha", "f1", "ob").Save(cancellationToken: token);
            await Lorry.Built("la", "f2", "ob").Save(cancellationToken: token);
        }

        using (Tenant.Use(Globex))
        {
            await Outfit.Named("ob").Save(cancellationToken: token);
            await Cistern.Built("cb", ("c2", "oa")).Save(cancellationToken: token);
        }
    }

    private static IEntityType WriteEntityOf(IModel writeModel, IReadOnlyEntityType face) =>
        writeModel.GetEntityTypes().Single(t =>
            t.ClrType.Namespace == face.ClrType.Namespace && t.ClrType.Name + "Read" == face.ClrType.Name);

    private static string Where(string sql) => sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

    private static string TableOf(DbContext read, Type face) => read.Model.FindEntityType(face)!.GetTableName()!;

    // Every table of the model the filter selects FROM, in name order.
    private static List<string> TablesAsked(DbContext read, string where) =>
        [.. read.Model.GetEntityTypes()
            .Select(static t => t.GetTableName()!)
            .Distinct(StringComparer.Ordinal)
            .Where(table => where.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    // The parents a face's tenant filter asks, read off the filter itself: one Set<TParent>() each.
    private static List<string> ParentsAsked(IReadOnlyEntityType face)
    {
        var filter = face.GetDeclaredQueryFilters().Single(static f => f.Key == "Tenant").Expression!;
        var sets = new SetCollector();
        sets.Visit(filter);
        return sets.Parents;
    }

    private sealed class SetCollector : ExpressionVisitor
    {
        public List<string> Parents { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(DbContext.Set) && node.Method.IsGenericMethod)
            {
                Parents.Add(node.Method.GetGenericArguments()[0].Name);
            }

            return base.VisitMethodCall(node);
        }

        // Once the model is built, EF Core holds each Set<TParent>() as a query root.
        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root)
            {
                Parents.Add(root.EntityType.ClrType.Name);
                return node;
            }

            return base.VisitExtension(node);
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
}
