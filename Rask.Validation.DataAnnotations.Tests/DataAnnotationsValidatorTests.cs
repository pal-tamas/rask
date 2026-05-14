using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Validation.DataAnnotations.Tests;

public class DataAnnotationsValidatorTests
{
    [Fact]
    public void Validate_PopulatesMessages_PerOffendingMember()
    {
        var p = new Person { Name = "", Age = 0, Code = "" };
        var ctx = RegisterValidator(p);

        var ok = ctx.Validate();

        Assert.False(ok);
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public void Validate_AllValid_ReturnsTrue()
    {
        var p = new Person { Name = "Ada", Age = 30, Code = "ABC" };
        var ctx = RegisterValidator(p);
        Assert.True(ctx.Validate());
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void ValidateField_OnlyValidatesThatField()
    {
        var p = new Person { Name = "", Age = 999, Code = "" };
        var ctx = RegisterValidator(p);

        ctx.ValidateField(new FieldIdentifier(p, "Age"));

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public void Component_Render_IsIdempotent_AcrossMultipleRenders()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);

        // Two separate component instances each Render under the same context: AddValidator's
        // type-dedup should prevent double-registration. If duplicated, "Name is required"
        // would appear twice in the messages list.
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
            DataAnnotationsValidator().ToHtml();
        }

        ctx.Validate();
        Assert.Single(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    // Pushes the scope, renders a DataAnnotationsValidator component (which registers its
    // Inner IFieldValidator into the context), and returns the context for further assertions.
    private static EditContext RegisterValidator(Person p)
    {
        var ctx = new EditContext(p);
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
        }
        return ctx;
    }

    private sealed class Person
    {
        [Required(ErrorMessage = "Name is required")] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
        [StringLength(5)] public string Code { get; set; } = "";
    }
}
