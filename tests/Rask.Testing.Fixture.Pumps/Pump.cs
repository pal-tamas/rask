using Rask.Data;

namespace Rask.Testing.Fixture.Pumps;

/// <summary>A pump: the one aggregate of this assembly, registered by its module initializer.</summary>
public sealed class Pump : Aggregate<Guid, Model>
{
    private Pump() { } // EF materialization

    public string Name { get; private set; } = "";
}
