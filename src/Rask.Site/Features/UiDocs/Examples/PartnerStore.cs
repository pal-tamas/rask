namespace Rask.Site.Features.UiDocs.Examples;

/// <summary>What a database of partners would answer, for the select's server-search examples.</summary>
internal static class PartnerStore
{
    private static readonly string[] Names =
    [
        "Alpine Freight", "Atlas Haulage", "Baltic Shipping", "Bergmann Logistik", "Berlin Cargo", "Blue Harbour",
        "Carpathian Transit", "Coastline Movers", "Danube Express", "Delta Couriers", "Eastgate Supply", "Falcon Fleet",
        "Granite Road", "Harbourline", "Iberia Trucking", "Kestrel Carriers", "Lakeside Depot", "Meridian Lines",
        "Northwind Haulers", "Orbit Parcel", "Pannonia Trans", "Quayside Storage", "Riverbend Logistics",
        "Summit Movers", "Tisza Freight", "Union Pallets", "Vistula Cargo", "Westbrook Supply", "Zenith Express",
    ];

    /// <summary>The partners whose name holds <paramref name="typed" />, in the order the store keeps them.</summary>
    /// <param name="typed">What was typed; empty for every partner.</param>
    internal static IEnumerable<(int Id, string Name)> Named(string typed) =>
        Names.Select((name, index) => (Id: index + 1, Name: name))
            .Where(partner => partner.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase));
}
