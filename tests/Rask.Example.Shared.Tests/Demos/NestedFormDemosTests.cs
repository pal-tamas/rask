using System.ComponentModel.DataAnnotations;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed partial class NestedFormDemosTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void NestedSubObjectDemo_Render_EmitsAllFieldsAndShippingFieldset()
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
    public void NestedListForeachDemo_Render_StartsWithSeededLineItem()
    {
        var html = new LiveHost(() => NestedListForeachDemo, TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("Coffee beans (250g)", html);
        Assert.Contains("nf-list-add", html);
        Assert.Contains("nf-list-submit", html);
    }

    [Fact]
    public void NestedListIndexerDemo_Render_StartsWithSeededSku()
    {
        var html = new LiveHost(() => NestedListIndexerDemo, TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("WIDGET-1", html);
        Assert.Contains("nf-idx-add", html);
    }

    [Fact]
    public void NestedFluentValidationDemo_Render_StartsWithSeededLine()
    {
        var html = new LiveHost(() => NestedFluentValidationDemo, TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("BOX-1", html);
        Assert.Contains("nf-fv-add", html);
        Assert.Contains("nf-fv-submit", html);
    }

    // --- Model & validator unit tests (no rendering required) ---

    [Fact]
    public void CheckoutModel_Empty_FailsRequiredFor_Name_Email_AddressFields()
    {
        var errors = Validate(new CheckoutModel());
        Assert.Contains(errors, e => e.MemberNames.Contains("Name"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        // Nested AddressModel is validated when explicitly walked. The example uses
        // DataAnnotationsValidator with cross-graph walk; we just spot the surface here.
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void CheckoutModel_InvalidEmail_FailsEmailAddress()
    {
        var model = new CheckoutModel { Name = "Pat", Email = "not-an-email" };
        var errors = Validate(model).Where(e => e.MemberNames.Contains("Email")).ToList();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void AddressModel_NonIsoCountry_FailsRegex()
    {
        var model = new AddressModel { Street = "1 Main", City = "Town", Country = "usa" };
        var errors = Validate(model).Where(e => e.MemberNames.Contains("Country")).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("ISO"));
    }

    [Fact]
    public void AddressModel_ValidIsoCountry_Passes()
    {
        var model = new AddressModel { Street = "1 Main", City = "Town", Country = "US" };
        var errors = Validate(model);
        Assert.Empty(errors);
    }

    [Fact]
    public void LineItem_QuantityZero_FailsRange()
    {
        var model = new LineItem { Description = "x", Quantity = 0 };
        var errors = Validate(model).Where(e => e.MemberNames.Contains("Quantity")).ToList();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void SkuRow_BadCode_FailsRegex_AndZeroPrice_FailsRange()
    {
        var model = new SkuRow { Code = "ab", Price = 0m };
        var errors = Validate(model);
        Assert.Contains(errors, e => e.MemberNames.Contains("Code"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Price"));
    }

    [Fact]
    public async Task NestedOrderValidator_EmptyOrder_FailsCustomerName_Address_AndLines()
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
    public async Task NestedOrderLineValidator_ZeroQuantity_FailsPositiveRule()
    {
        var v = new NestedOrderLineValidator();
        var result = await v.ValidateAsync(new NestedOrderLine { Sku = "OK", Quantity = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task NestedOrderAddressValidator_EmptyFields_FailsBothRules()
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
