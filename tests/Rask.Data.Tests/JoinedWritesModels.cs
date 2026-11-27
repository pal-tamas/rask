using System.ComponentModel.DataAnnotations;

namespace Rask.Data.Tests;

public readonly partial record struct HayLoadId(int Value);

// A load of hay brought in from a field, born through its factory's generated entry with a key the store makes — what a
// Db.Transaction around several writes calls first, and whose id the rows added after it need (DbTransactionJoinedWritesTests).
public sealed partial class HayLoad : Aggregate<HayLoadId>, IHasTimestamps, IVersioned, IDeletable
{
    private HayLoad() { } // EF materialization

    [Required, StringLength(20)]
    public string Field { get; private set; } = "";

    [Range(1, 100)]
    public int Bales { get; private set; }

    private static HayLoad Deliver(string meadow, int bales) => new() { Field = meadow, Bales = bales };
}

// A bale of a load, tagged: the rows an app adds on its own context after the load's entry, or clears with DeleteWhere.
public sealed class HayBale : Aggregate<Guid>, IHasTimestamps, IVersioned, IDeletable
{
    private HayBale() { } // EF materialization

    public HayLoadId Load { get; private set; }

    public string Tag { get; private set; } = "";

    public static HayBale Of(HayLoadId load, string tag) => new() { Id = Guid.CreateVersion7(), Load = load, Tag = tag };
}
