using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Outbox.Tests;

public sealed record ParcelsWeighed(IReadOnlyList<int> Weights) : IEvent;

// A parcel whose weight is projected into ParcelsWeighed (ProjectionOutboxTests).
public sealed class Parcel : Aggregate<Guid>
{
    private Parcel() { } // EF materialization

    public int Weight { get; private set; }

    public static Parcel Weighed(int weight) => new() { Id = Guid.CreateVersion7(), Weight = weight };

    public static void Configure(EntityTypeBuilder<Parcel> builder) =>
        builder.Projects(p => p.Weight, changed => new ParcelsWeighed(changed.Values(p => p.Weight)));
}

public sealed class RecordWeights(ShippingLog log) : IDurableHandler<ParcelsWeighed>
{
    public Task Handle(ParcelsWeighed e)
    {
        log.Add($"weighed:{string.Join(',', e.Weights)}");
        return Task.CompletedTask;
    }
}
