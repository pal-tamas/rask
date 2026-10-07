using System.ComponentModel.DataAnnotations;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class FloatingLabelsDemoTests : global::Rask.Core.RaskMarkup
{
    // A valid submit must re-render the consumer (FloatingLabelsDemo) so its success alert — which
    // lives OUTSIDE the Form — appears. OnSubmit sets the demo's _submission; the Form must
    // re-render the callback's owner. Regression guard for the submit-success-not-shown bug.
    [Fact]
    public async Task A_valid_submit_shows_the_success_alert()
    {
        var page = Page.Render(() => FloatingLabelsDemo, TestServices.Default());
        var html = page.Render();
        // Populate the model through the live field handlers (the submit bridge validates/invokes
        // against the live-bound model, not the event payload).
        await Fill(page, html, "ff-FullName", "Ada Lovelace");
        await Fill(page, html, "ff-Email", "ada@example.com");
        await Fill(page, html, "ff-Age", "30");
        await Fill(page, html, "ff-Plan", "pro");

        await page.Invoke(SubmitHandler(page.Render()));

        var final = page.Render();
        Assert.Contains("Created account for Ada Lovelace", final);
        Assert.Contains("data-ui-callout role=\"status\"", final, StringComparison.Ordinal);
    }

    private static async Task Fill(Page page, string html, string id, string value)
    {
        foreach (var attr in new[] { "data-rask-on-input", "data-rask-on-change" })
        {
            var hid = TryAttrOnTagWith(html, $"id=\"{id}\"", attr);
            if (hid is not null)
            {
                await page.Invoke(hid, $"{{\"value\":\"{value}\"}}");
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
    public void The_FloatingLabelsDemo_links_every_label_to_its_control()
    {
        var html = Page.Render(() => FloatingLabelsDemo, TestServices.Default()).Html;

        // All three controls render. The assertion is on the TAGS, which a restyle is not entitled to change.
        Assert.Contains("<input ", html);
        Assert.Contains("<textarea ", html);
        Assert.Contains("<select ", html);

        // The input, the textarea and the select are Flux's now, with the label over the field: nothing floats
        // any more. Each label is linked to its control by for/id. The ff-* ids are the browser journey's selectors.
        Assert.DoesNotContain("class=\"floating-label\"", html);
        foreach (var prop in new[] { "FullName", "Email", "Age", "Plan", "Bio" })
        {
            Assert.Contains($"id=\"ff-{prop}\"", html);
            Assert.Contains($"for=\"ff-{prop}\"", html);
        }

        Assert.Contains("<label id=\"ff-FullName-label\"", html);
        Assert.Contains("<label id=\"ff-Bio-label\"", html);

        Assert.Contains(">Create account<", html);
        // No messages until a failed submit.
        Assert.DoesNotContain("is required", html);
    }

    [Fact]
    public void An_empty_AccountModel_fails_its_required_fields()
    {
        var errors = Validate(new AccountModel());

        Assert.Contains(errors, e => e.MemberNames.Contains("FullName"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
        Assert.Contains(errors, e => e.MemberNames.Contains("Plan"));
    }

    [Fact]
    public void An_AccountModel_with_valid_values_has_no_errors()
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
