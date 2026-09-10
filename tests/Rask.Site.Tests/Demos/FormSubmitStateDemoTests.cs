using System.Text.Json;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     A BOUND kit field inside a <c>Form</c> writes what was typed back to the model.
/// </summary>
/// <remarks>
///     <para>
///     Added because the browser journey caught this demo failing to save after its label and input were
///     merged into one <c>UiInput</c>, and the E2E failure said only that a readout on the other side of
///     the page still read "(nothing yet)" — ten seconds per attempt and nothing about which half was
///     broken. This drives the change handler directly, so it answers the actual question: does the value
///     reach the model.
///     </para>
///     <para>
///     It is in the site's suite rather than the kit's on purpose. The kit's binding tests render a control
///     on its own; what failed here needs the whole arrangement — a bound kit field, inside a real
///     <c>Form</c>, inside a children FUNCTION that takes the submitting flag.
///     </para>
/// </remarks>
public sealed partial class FormSubmitStateDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Typing_into_the_bound_field_reaches_the_model()
    {
        var page = RaskTest.Render(() => FormSubmitStateDemo, TestServices.Default());
        var html = page.Render();

        // The field renders, is labelled, and is the one the browser suite selects on.
        Assert.Contains("id=\"fss-input\"", html, StringComparison.Ordinal);
        Assert.Contains("Username", html, StringComparison.Ordinal);

        var id = HandlerIn(html, "id=\"fss-input\"", "data-rask-on-input");
        await page.InvokeAsync(id, $"{{\"value\":\"ada\"}}");

        // The model took it. Rendering again is what proves the write-back landed rather than the handler
        // merely existing — a handler that runs and writes nowhere looks identical in the markup.
        Assert.Contains("ada", page.Render(), StringComparison.Ordinal);
    }

    private static string HandlerIn(string html, string anchor, string attr)
    {
        var marker = attr + "=\"";

        foreach (var tag in html.Split('<'))
        {
            if (!tag.Contains(anchor, StringComparison.Ordinal) ||
                !tag.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var s = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return tag[s..tag.IndexOf('"', s)];
        }

        throw new InvalidOperationException($"No '{attr}' handler on the tag containing '{anchor}'.");
    }
}
