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

        // Bootstrap floating-label markup across all three controls.
        Assert.Contains("form-floating", html);
        Assert.Contains("form-control", html);   // input + textarea
        Assert.Contains("form-select", html);    // select

        // Ids are derived from the bound property name (ff-{Property}) and the label links to them.
        foreach (var prop in new[] { "FullName", "Email", "Age", "Plan", "Bio" })
        {
            Assert.Contains($"id=\"ff-{prop}\"", html);
            Assert.Contains($"for=\"ff-{prop}\"", html);
        }

        // Labels come from the model's [Display(Name)] attributes, not the property names.
        Assert.Contains(">Full name<", html);
        Assert.Contains(">Email address<", html);
        Assert.Contains(">Short bio<", html);

        Assert.Contains(">Create account<", html);
        // No messages until a failed submit.
        Assert.DoesNotContain("invalid-feedback", html);
    }

    [Fact]
    public void AccountModel_Empty_FailsRequiredFields()
    {
        var errors = Validate(new AccountModel());
        Assert.Contains(errors, e => e.MemberNames.Contains("FullName"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Plan"));
    }

    [Fact]
    public void AccountModel_ValidValues_HasNoErrors()
    {
        var model = new AccountModel
        {
            FullName = "Pat Lee",
            Email = "pat@example.com",
            Age = 30,
            Plan = "pro",
            Bio = "Hello"
        };
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
