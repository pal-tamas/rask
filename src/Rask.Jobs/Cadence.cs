namespace Rask.Background;

/// <summary>How often a calendar schedule comes round.</summary>
public enum Cadence
{
    /// <summary>Once a day, at the given time.</summary>
    Daily,

    /// <summary>Once a week, on the given day.</summary>
    Weekly,

    /// <summary>Once a month, on the given date.</summary>
    Monthly,
}
