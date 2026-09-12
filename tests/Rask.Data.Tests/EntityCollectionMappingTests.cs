using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The RASK085 shape: the entity keeps its lines in a private readonly field, hands out a read-only view,
// and is the only thing that can add one. Nothing below configures anything — no HasMany, no HasField, no
// UsePropertyAccessMode, and no key configuration either — which is the claim under test.
public sealed class Cart : Model<Guid>
{
    private readonly List<CartLine> _lines = [];

    private Cart() { } // EF materialization

    public string Owner { get; private set; } = "";

    public IReadOnlyCollection<CartLine> Lines => _lines;

    public static Cart Open(string owner) => new() { Id = Guid.NewGuid(), Owner = owner };

    public CartLine Add(string product, int quantity)
    {
        var line = CartLine.For(product, quantity);
        _lines.Add(line);
        return line;
    }
}

public sealed class CartLine : Model<Guid>
{
    private CartLine() { } // EF materialization

    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    // The line's key is set here, by the aggregate, not by the store. Left to EF Core's own convention a Guid
    // key is ValueGenerated.OnAdd, and a line DetectChanges finds in an already-tracked cart with its Id already
    // set is taken for an existing row and UPDATEd — "expected to affect 1 row(s), but actually affected 0".
    // Rask.Data's key convention marks every non-integer Model<TId> key never generated, so it is INSERTed; the
    // last test below fails without that convention.
    internal static CartLine For(string product, int quantity) =>
        new() { Id = Guid.NewGuid(), Product = product, Quantity = quantity };
}

[Collection(DataDbCollection.Name)]
public sealed class EntityCollectionMappingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-cart-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_read_only_collection_is_mapped_as_a_navigation_through_its_backing_field()
    {
        await using var database = await StartDatabaseAsync();

        var cart = database.Context.Model.FindEntityType(typeof(Cart))!;
        var navigation = cart.FindNavigation(nameof(Cart.Lines));

        Assert.NotNull(navigation);
        Assert.True(navigation.IsCollection);
        Assert.Equal(typeof(CartLine), navigation.TargetEntityType.ClrType);
        Assert.Equal("_lines", navigation.FieldInfo?.Name);

        // The dependent gets a shadow foreign key; nothing had to be declared on CartLine for it.
        var line = database.Context.Model.FindEntityType(typeof(CartLine))!;
        Assert.True(line.FindProperty("CartId")!.IsShadowProperty());

        // Nor for its key: the convention, not a Configure, is what keeps a factory-set Id from reading as
        // "this row exists".
        Assert.Equal(
            Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            line.FindPrimaryKey()!.Properties.Single().ValueGenerated);
    }

    [Fact]
    public async Task Lines_added_through_the_domain_method_round_trip_against_SQLite()
    {
        await using var database = await StartDatabaseAsync();

        var cart = Cart.Open("ada");
        cart.Add("apple", 3);
        cart.Add("pear", 1);
        database.Context.Add(cart);
        await database.Context.SaveChangesAsync();

        // The model surface opens a context of its own, so this reads what was saved, not what is tracked.
        var loaded = await Cart.All.QueryAsync((q, ct) => q.Include(c => c.Lines).SingleAsync(ct));

        Assert.Equal("ada", loaded.Owner);
        Assert.Equal(
            [("apple", 3), ("pear", 1)],
            loaded.Lines.OrderBy(l => l.Product, StringComparer.Ordinal).Select(l => (l.Product, l.Quantity)));
    }

    [Fact]
    public async Task A_line_added_to_a_loaded_cart_is_inserted_on_save()
    {
        await using var database = await StartDatabaseAsync();

        var cart = Cart.Open("grace");
        cart.Add("apple", 1);
        database.Context.Add(cart);
        await database.Context.SaveChangesAsync();

        // A fresh context, the way a request handler sees the row: load, change through the entity, save.
        await using (var context = new RaskDbContext(Options()))
        {
            var loaded = await context.Set<Cart>().Include(c => c.Lines).SingleAsync(c => c.Id == cart.Id);
            loaded.Add("pear", 2);
            await context.SaveChangesAsync();
        }

        await using (var context = new RaskDbContext(Options()))
        {
            var reloaded = await context.Set<Cart>().Include(c => c.Lines).SingleAsync(c => c.Id == cart.Id);
            Assert.Equal(
                ["apple", "pear"],
                reloaded.Lines.Select(l => l.Product).Order(StringComparer.Ordinal));
        }
    }

    private DbContextOptions<RaskDbContext> Options() =>
        new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
