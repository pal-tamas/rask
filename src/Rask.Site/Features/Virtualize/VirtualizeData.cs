namespace Rask.Site.Features;

public static class VirtualizeData
{
    public static readonly IReadOnlyList<VirtualizeRow> Rows = Build(10_000);

    private static VirtualizeRow[] Build(int count)
    {
        var firsts = new[]
        {
            "Ada", "Grace", "Linus", "Margaret", "Donald", "Barbara", "Edsger", "Tony", "Alan", "John"
        };
        var lasts = new[]
        {
            "Lovelace", "Hopper", "Torvalds", "Hamilton", "Knuth", "Liskov", "Dijkstra", "Hoare", "Turing", "Backus"
        };
        var cities = new[]
        {
            "London", "New York", "Helsinki", "Boston", "Stanford", "Cambridge", "Amsterdam", "Oxford",
            "Manchester", "Berkeley"
        };
#pragma warning disable S2245 // seeded on purpose: the same demo data on every visit, nothing secret
        var rng = new Random(42);
#pragma warning restore S2245
        var rows = new VirtualizeRow[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"{firsts[i % firsts.Length]} {lasts[i / firsts.Length % lasts.Length]} #{i + 1:D5}";
            rows[i] = new VirtualizeRow(i + 1, name, cities[rng.Next(cities.Length)], rng.Next(0, 100000) / 100m);
        }

        return rows;
    }
}
