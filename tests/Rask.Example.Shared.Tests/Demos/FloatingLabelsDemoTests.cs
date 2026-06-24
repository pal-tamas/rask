using System.ComponentModel.DataAnnotations;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class FloatingLabelsDemoTests
{
    [Fact]
    public void FloatingLabelsDemo_Render_EmitsFloatingFieldsAndLinkedLabels()
    {
        var html = new LiveHost(() => FloatingLabelsDemo(), TestServices.Default()).RenderAsLiveRoot();

        // Bootstrap floating-label markup: a .form-floating wrapper around a .form-control input.
        Assert.Contains("form-floating", html);
        Assert.Contains("form-control", html);

        // Ids are derived from the bound property name (ff-{Property}) and the label links to them.
        foreach (var prop in new[] { "FullName", "Email", "Age" })
        {
            Assert.Contains($"id=\"ff-{prop}\"", html);
            Assert.Contains($"for=\"ff-{prop}\"", html);
        }

        Assert.Contains(">Create account<", html);
        // No messages until a failed submit.
        Assert.DoesNotContain("invalid-feedback", html);
    }

    [Fact]
    public void AccountModel_Empty_FailsRequiredAndAgeRange()
    {
        var errors = Validate(new AccountModel());
        Assert.Contains(errors, e => e.MemberNames.Contains("FullName"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Age"));
    }

    [Fact]
    public void AccountModel_ValidValues_HasNoErrors()
    {
        var model = new AccountModel { FullName = "Pat Lee", Email = "pat@example.com", Age = 30 };
        Assert.Empty(Validate(model));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, ctx, results, true);
        return results;
    }
}
