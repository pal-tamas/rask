using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

// The EF Core mapping for Order (keeps the domain model free of persistence attributes).
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Customer).HasConversion(v => v.Value, s => OrderCustomer.Create(s)).HasMaxLength(OrderCustomer.MaxLength);
    }
}
