namespace Rask.Site.Features;

public sealed class ItineraryModel
{
    public string Traveller { get; set; } = "";

    public string Name { get; set; } = "";

    public string Notes { get; set; } = "";

    /// <summary>How many travel: a whole number, or nothing yet.</summary>
    public int? Travellers { get; set; }

    /// <summary>The cabin picked from the list.</summary>
    public string Cabin { get; set; } = "";

    /// <summary>Whether the trip is insured.</summary>
    public bool Insured { get; set; }
}
