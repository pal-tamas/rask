namespace Rask.Site.Features;

// The demo's fuel purchases: three trucks over a quarter, the same every time it is built.
public static class RefundData
{
    private static readonly string[] Stations =
        ["Győr, Fehérvári út", "Mosonmagyaróvár, Lajta út", "Tatabánya, Csaba utca", "Budaörs, Szabadság út"];

    public static IReadOnlyList<Vehicle> Vehicles { get; } =
    [
        Truck("ŐRZ-104", "Volvo FH 500", seed: 1),
        Truck("ÜGY-731", "Scania R 450", seed: 2),
        Truck("ÁRV-215", "MAN TGX 18.510", seed: 3),
    ];

    private static Vehicle Truck(string plate, string model, int seed) =>
        new(plate, model, [.. Enumerable.Range(1, 15).Select(n => Purchase(plate, seed, n))]);

    private static Row Purchase(string plate, int seed, int n)
    {
        var litres = 180 + ((n * 37 + seed * 11) % 220) + 0.5m;
        return new Row(
            new DateOnly(2026, 7, 1).AddDays((n * 3 + seed) % 92),
            $"SZ-{seed}{n:D4}-{plate[..3]}",
            Stations[(n + seed) % Stations.Length],
            litres,
            decimal.Round(litres * 33.4m));
    }

    public sealed record Vehicle(string Plate, string Model, IReadOnlyList<Row> Rows);

    public sealed record Row(DateOnly Day, string Invoice, string Station, decimal Litres, decimal Refund);
}
