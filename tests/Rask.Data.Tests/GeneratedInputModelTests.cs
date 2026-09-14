using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Rask.Data.Tests;

// An entity shaped the way the guide shows one — private constructor, private setters, a validation attribute,
// a value object, a counter the application owns — so these drive the form model the source generator really
// emitted for it in this compilation.
public sealed class Invoice : Model<Guid>, ITimestamped, IVersioned
{
    private Invoice() { }

    [Required]
    [MaxLength(40)]
    public string Title { get; private set; } = "";

    public decimal Balance { get; private set; }

    public InvoiceTotal Total { get; private set; } = new(0m, "EUR");

    [SkipModel]
    public int Views { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public int Version { get; private set; }

    public void Viewed() => Views++;
}

public sealed record InvoiceTotal(decimal Amount, string Currency) : IValueObject;

// Builds no context, but the guard wants every class in the collection or exempt by name; collecting it is cheaper.
[Collection(DataDbCollection.Name)]
public sealed class GeneratedInputModelTests
{
    private static InvoiceModel NewModel() => new()
    {
        Title = "March",
        Balance = 120.5m,
        Total = new InvoiceModel.InvoiceTotalModel { Amount = 99m, Currency = "HUF" },
    };

    private static bool IsValid(object model, out List<ValidationResult> results)
    {
        results = [];
        return Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
    }

    [Fact]
    public void The_model_carries_the_mapped_values_and_the_version_but_no_id_and_no_framework_columns()
    {
        var names = typeof(InvoiceModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Balance", "Title", "Total", "Version"], names);
        Assert.All(typeof(InvoiceModel).GetProperties(), p => Assert.True(p.CanWrite, p.Name));
    }

    [Fact]
    public void A_skipped_property_is_left_off_the_model()
    {
        Assert.Null(typeof(InvoiceModel).GetProperty(nameof(Invoice.Views)));
    }

    [Fact]
    public void A_value_object_becomes_a_nested_settable_model()
    {
        var total = typeof(InvoiceModel).GetProperty(nameof(InvoiceModel.Total))!;

        Assert.Equal(typeof(InvoiceModel.InvoiceTotalModel), total.PropertyType);
        Assert.NotNull(new InvoiceModel().Total);

        var model = NewModel();
        model.Total.Amount = 12m;
        Assert.Equal(12m, model.Total.Amount);
    }

    [Fact]
    public void The_entity_validation_attributes_are_checked_on_the_model()
    {
        Assert.True(IsValid(NewModel(), out _));

        var blank = NewModel();
        blank.Title = "";
        Assert.False(IsValid(blank, out var missing));
        Assert.Contains(missing, r => r.MemberNames.Contains(nameof(InvoiceModel.Title)));

        var tooLong = NewModel();
        tooLong.Title = new string('x', 41);
        Assert.False(IsValid(tooLong, out var overflow));
        Assert.Contains(overflow, r => r.MemberNames.Contains(nameof(InvoiceModel.Title)));
    }

    [Fact]
    public void Nothing_but_the_model_is_generated_for_the_entity()
    {
        // No generated writes, no ToModel(): the entity type carries only the reads, and the model only its values.
        var generated = typeof(Invoice).Assembly.GetTypes()
            .Select(t => t.FullName!)
            .Where(n => n.StartsWith("Rask.Data.Tests.InvoiceModel", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Rask.Data.Tests.InvoiceModel", "Rask.Data.Tests.InvoiceModel+InvoiceTotalModel"], generated);
        Assert.DoesNotContain(
            typeof(InvoiceModel).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            m => !m.IsSpecialName);
    }
}
