using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class FloatingLabelsDemoTests
{
    // A valid submit must re-render the consumer (FloatingLabelsDemo) so its success alert — which
    // lives OUTSIDE the Form — appears. OnValidSubmit sets the demo's _submission; the Form must
    // re-render the callback's owner. Regression guard for the submit-success-not-shown bug.
    [Fact]
    public async Task ValidSubmit_ShowsSuccessAlert()
    {
        var page = RaskTest.Render(() => FloatingLabelsDemo(), TestServices.Default());
        var html = page.Render();

        // Populate the model through the live field handlers (the submit bridge validates/invokes
        // against the live-bound model, not the event payload).
        await Fill(page, html, "ff-FullName", "Ada Lovelace");
        await Fill(page, html, "ff-Email", "ada@example.com");
        await Fill(page, html, "ff-Age", "30");
        await Fill(page, html, "ff-Plan", "pro");

        await page.InvokeAsync(SubmitHandler(page.Render()));

        var final = page.Render();
        Assert.Contains("Created account for Ada Lovelace", final);
        Assert.Contains("alert-success", final);
    }

    private static async Task Fill(RenderedComponent page, string html, string id, string value)
    {
        foreach (var attr in new[] { "data-rask-on-input", "data-rask-on-change" })
        {
            var hid = TryAttrOnTagWith(html, $"id=\"{id}\"", attr);
            if (hid is not null)
            {
                await page.InvokeAsync(hid, $"{{\"value\":\"{value}\"}}");
            }
        }
    }

    private static string SubmitHandler(string html)
    {
        const string marker = "data-rask-on-submit=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, "no form submit handler");
        i += marker.Length;
        return html[i..html.IndexOf('"', i)];
    }

    private static string? TryAttrOnTagWith(string html, string anchor, string attr)
    {
        var marker = attr + "=\"";
        foreach (var tag in html.Split('<'))
        {
            if (!tag.Contains(anchor, StringComparison.Ordinal) || !tag.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var s = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return tag[s..tag.IndexOf('"', s)];
        }

        return null;
    }

    [Fact]
    public void FloatingLabelsDemo_Render_EmitsFloatingFieldsAndLinkedLabels()
    {
        var html = RaskTest.Render(() => FloatingLabelsDemo(), TestServices.Default()).Html;

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
