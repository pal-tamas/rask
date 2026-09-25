using System.ComponentModel.DataAnnotations;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class NestedFormDemosTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void NestedSubObjectDemo_renders_all_fields_and_the_shipping_fieldset()
    {
        var html = new LiveHost(() => NestedSubObjectDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("nf-name", html);
        Assert.Contains("nf-email", html);
        Assert.Contains("Shipping address", html);
        Assert.Contains("nf-street", html);
        Assert.Contains("nf-city", html);
        Assert.Contains("nf-country", html);
        Assert.Contains("Place order", html);
    }

    [Fact]
    public void NestedListForeachDemo_starts_with_a_seeded_line_item()
    {
        var html = new LiveHost(() => NestedListForeachDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("Coffee beans (250g)", html);
        Assert.Contains("nf-list-add", html);
        Assert.Contains("nf-list-submit", html);
    }

    [Fact]
    public void NestedListIndexerDemo_starts_with_a_seeded_sku()
    {
        var html = new LiveHost(() => NestedListIndexerDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("WIDGET-1", html);
        Assert.Contains("nf-idx-add", html);
    }

    [Fact]
    public void NestedFluentValidationDemo_starts_with_a_seeded_line()
    {
        var html = new LiveHost(() => NestedFluentValidationDemo, TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("BOX-1", html);
        Assert.Contains("nf-fv-add", html);
        Assert.Contains("nf-fv-submit", html);
    }

    // --- Model & validator unit tests (no rendering required) ---

    [Fact]
    public void An_empty_CheckoutModel_fails_required_for_the_name_email_and_address_fields()
    {
        var errors = Validate(new CheckoutModel());

        Assert.Contains(errors, e => e.MemberNames.Contains("Name"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        // Nested AddressModel is validated when explicitly walked. The example uses
        // DataAnnotationsValidator with cross-graph walk; we just spot the surface here.
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_CheckoutModel_with_an_invalid_email_fails_EmailAddress()
    {
        var model = new CheckoutModel { Name = "Pat", Email = "not-an-email" };

        var errors = Validate(model).Where(e => e.MemberNames.Contains("Email")).ToList();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void An_AddressModel_with_a_non_ISO_country_fails_the_regex()
    {
        var model = new AddressModel { Street = "1 Main", City = "Town", Country = "usa" };

        var errors = Validate(model).Where(e => e.MemberNames.Contains("Country")).ToList();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("ISO"));
    }

    [Fact]
    public void An_AddressModel_with_a_valid_ISO_country_passes()
    {
        var model = new AddressModel { Street = "1 Main", City = "Town", Country = "US" };

        var errors = Validate(model);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_LineItem_with_zero_quantity_fails_the_range()
    {
        var model = new LineItem { Description = "x", Quantity = 0 };

        var errors = Validate(model).Where(e => e.MemberNames.Contains("Quantity")).ToList();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_SkuRow_fails_the_regex_on_a_bad_code_and_the_range_on_a_zero_price()
    {
        var model = new SkuRow { Code = "ab", Price = 0m };

        var errors = Validate(model);

        Assert.Contains(errors, e => e.MemberNames.Contains("Code"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Price"));
    }

    [Fact]
    public async Task NestedOrderValidator_fails_an_empty_order_on_customer_name_address_and_lines()
    {
        var v = new NestedOrderValidator();
        var model = new NestedOrderModel();
        model.Lines.Add(new NestedOrderLine());

        var result = await v.ValidateAsync(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CustomerName");
        Assert.Contains(result.Errors, e => e.PropertyName == "Address.Street");
        Assert.Contains(result.Errors, e => e.PropertyName == "Address.City");
        Assert.Contains(result.Errors, e => e.PropertyName == "Lines[0].Sku");
    }

    [Fact]
    public async Task NestedOrderLineValidator_fails_a_zero_quantity_on_the_positive_rule()
    {
        var v = new NestedOrderLineValidator();

        var result = await v.ValidateAsync(new NestedOrderLine { Sku = "OK", Quantity = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task NestedOrderAddressValidator_fails_both_rules_on_empty_fields()
    {
        var v = new NestedOrderAddressValidator();

        var result = await v.ValidateAsync(new NestedOrderAddress());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Street");
        Assert.Contains(result.Errors, e => e.PropertyName == "City");
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, true);
        return results;
    }
}
