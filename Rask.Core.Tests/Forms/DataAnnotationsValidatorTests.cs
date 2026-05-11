using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class DataAnnotationsValidatorTests
{
    [Fact]
    public void Validate_PopulatesMessages_PerOffendingMember()
    {
        var ctx = new EditContext(new Person { Name = "", Age = 0, Code = "" });
        ctx.AddValidator(new DataAnnotationsValidator());

        var ok = ctx.Validate();

        Assert.False(ok);
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(ctx.Model, "Name")));
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(ctx.Model, "Age")));
    }

    [Fact]
    public void Validate_AllValid_ReturnsTrue()
    {
        var ctx = new EditContext(new Person { Name = "Ada", Age = 30, Code = "ABC" });
        ctx.AddValidator(new DataAnnotationsValidator());
        Assert.True(ctx.Validate());
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void ValidateField_OnlyValidatesThatField()
    {
        var p = new Person { Name = "", Age = 999, Code = "" };
        var ctx = new EditContext(p);
        ctx.AddValidator(new DataAnnotationsValidator());

        ctx.ValidateField(new FieldIdentifier(p, "Age"));

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    private sealed class Person
    {
        [Required] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
        [StringLength(5)] public string Code { get; set; } = "";
    }
}
