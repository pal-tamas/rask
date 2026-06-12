using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Example.EfCore.Features.Catalog.Shared;

// EF Core entity configuration: maps the aggregate's value objects onto SQLite columns via value
// converters. Keeping the mapping in an IEntityTypeConfiguration (applied with
// ApplyConfigurationsFromAssembly) keeps the domain model free of persistence concerns.
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.HasKey(p => p.Id);

        entity.Property(p => p.Name)
            .HasConversion(name => name.Value, value => ProductName.From(value))
            .IsRequired()
            .HasMaxLength(ProductName.MaxLength);

        // Money <-> INTEGER minor units. No decimal column at all (SQLite has no decimal type).
        entity.Property(p => p.Price)
            .HasConversion(money => money.Cents, cents => Money.FromCents(cents));

        entity.Property(p => p.Stock)
            .HasConversion(stock => stock.Value, value => StockLevel.From(value));
    }
}
