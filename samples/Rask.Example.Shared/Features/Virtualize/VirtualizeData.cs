namespace Rask.Example.Shared.Features;

// Shared sample data for the /virtualize demos. Deliberately kept out of the code-sample
// tabs (the demos reference VirtualizeData.Rows) so each snippet stays focused on the
// VirtualizeModel usage rather than the row-building boilerplate.
public sealed record VirtualizeRow(int Index, string Name, string City, decimal Balance);

public static class VirtualizeData
{
    public static readonly VirtualizeRow[] Rows = Build(10_000);

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
        var rng = new Random(42);
        var rows = new VirtualizeRow[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"{firsts[i % firsts.Length]} {lasts[i / firsts.Length % lasts.Length]} #{i + 1:D5}";
            rows[i] = new VirtualizeRow(i + 1, name, cities[rng.Next(cities.Length)], rng.Next(0, 100000) / 100m);
        }

        return rows;
    }
}
