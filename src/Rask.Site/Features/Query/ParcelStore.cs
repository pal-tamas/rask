namespace Rask.Site.Features;

// The data behind the Rask.Query demo: a query for a page of parcels, one for a single parcel, and a command
// that ships one. Each handler waits a moment, as a network call would, so the loading and pending states are there to
// see. The store is scoped, so every visitor ships their own parcels.
public sealed class ParcelStore
{
    private static readonly string[] Recipients = ["Ada", "Grace", "Linus", "Margaret", "Ken", "Barbara"];
    private static readonly string[] Contents = ["books", "a keyboard", "tea", "a lamp", "records", "a kite"];

    private readonly List<Parcel> _parcels = Enumerable.Range(1, 12)
        .Select(i => new Parcel(i, Recipients[(i - 1) % 6], Contents[(i * 5) % 6], Shipped: false))
        .ToList();

    public const int PageSize = 4;

    public int Pages => (_parcels.Count + PageSize - 1) / PageSize;

    public IReadOnlyList<Parcel> Page(int page) => _parcels.Skip((page - 1) * PageSize).Take(PageSize).ToList();

    public Parcel? Find(int id) => _parcels.Find(p => p.Id == id);

    public void Ship(int id)
    {
        var index = _parcels.FindIndex(p => p.Id == id);
        if (index >= 0)
        {
            _parcels[index] = _parcels[index] with { Shipped = true };
        }
    }

    // Not a CQRS query: the kind of local data source a function query wraps.
    public async Task<IReadOnlyList<Parcel>> SearchAsync(string contents, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
        return _parcels.Where(p => p.Contents.Contains(contents, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
