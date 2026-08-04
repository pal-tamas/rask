using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

// The EF Core mapping for Product (keeps the domain model free of persistence attributes).
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasConversion(v => v.Value, s => ProductName.Create(s)).HasMaxLength(ProductName.MaxLength);
    }
}
