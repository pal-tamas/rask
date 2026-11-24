namespace Rask.Testing.Database.Tests;

/// <summary>Where a tank's stock count starts: a day and a quantity, either of which an older row may lack.</summary>
public sealed partial record CountStart(DateTime? Date, decimal? Quantity) : Value;

/// <summary>A delivered part of a tank, which may open the tank's count — TA's supply part, as its legacy rows hold it.</summary>
public sealed class TankPart : Aggregate<Guid>
{
    private TankPart() { } // EF materialization

    public string Tank { get; private set; } = "";

    /// <summary>Where the count starts; null when it does not start here.</summary>
    public CountStart? Start { get; private set; }

    public static TankPart For(string tank, CountStart? start) => new() { Id = Guid.NewGuid(), Tank = tank, Start = start };

    public void Opens(CountStart? start) => Start = start;
}
