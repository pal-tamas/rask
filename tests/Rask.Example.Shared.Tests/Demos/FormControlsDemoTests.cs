using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// The /form-controls showcase page demonstrates every control in both shapes — controlled (Value +
// OnChange) and bound (two-way Bind). These tests drive the live change/input/click handlers directly and
// assert the derived readout updates, proving the consumer re-renders for every (control × mode). The
// controlled cases are the regression guard for the controlled-OnChange dirty-mark fix; the bound cases
// pin two-way parity. The full browser walk is covered in SharedSmokeTests.
public sealed partial class FormControlsDemoTests : global::Rask.Core.RaskMarkup
{
    // ---- Select ----

    [Fact]
    public async Task Select_Controlled_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsSelectDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Picked: <strong>Rask</strong>", html);

        var id = HandlerIn(html, "id=\"fc-select-controlled\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("Blazor"));

        Assert.Contains("Picked: <strong>Blazor</strong>", page.Render());
    }

    [Fact]
    public async Task Select_Bound_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsSelectDemo, TestServices.Default());
        var html = page.Render();

        var id = HandlerIn(html, "id=\"fc-select-bound\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("htmx"));

        var html2 = page.Render();
        Assert.Contains("fc-select-bound-out", html2);
        Assert.Contains("Picked: <strong>htmx</strong>", html2);
    }

    // ---- Input (text) ----

    [Fact]
    public async Task Input_Controlled_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsInputDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Echo: <strong>(empty)</strong>", html);

        var id = HandlerIn(html, "id=\"fc-input-controlled\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("hello"));

        Assert.Contains("Echo: <strong>hello</strong>", page.Render());
    }

    [Fact]
    public async Task Input_Bound_OnInput_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsInputDemo, TestServices.Default());
        var html = page.Render();

        // A bound text Input streams via data-rask-on-input (per keystroke); the change handler only touches.
        var id = HandlerIn(html, "id=\"fc-input-bound\"", "data-rask-on-input");
        await page.InvokeAsync(id, Value("world"));

        Assert.Contains("Echo: <strong>world</strong>", page.Render());
    }

    // ---- Textarea ----

    [Fact]
    public async Task Textarea_Controlled_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsTextareaDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Length: <strong>0</strong>", html);

        var id = HandlerIn(html, "id=\"fc-textarea-controlled\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("abcd"));

        Assert.Contains("Length: <strong>4</strong>", page.Render());
    }

    [Fact]
    public async Task Textarea_Bound_OnInput_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsTextareaDemo, TestServices.Default());
        var html = page.Render();

        var id = HandlerIn(html, "id=\"fc-textarea-bound\"", "data-rask-on-input");
        await page.InvokeAsync(id, Value("abc"));

        var html2 = page.Render();
        Assert.Contains("fc-textarea-bound-out", html2);
        Assert.Contains("Length: <strong>3</strong>", html2);
    }

    // ---- BsRadioGroup ----

    [Fact]
    public async Task Radio_Controlled_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsRadioDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Plan: <strong>Free</strong>", html);

        // Controlled group renders first → first occurrence of value="Pro".
        var id = HandlerIn(html, "value=\"Pro\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("true"));

        Assert.Contains("Plan: <strong>Pro</strong>", page.Render());
    }

    [Fact]
    public async Task Radio_Bound_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsRadioDemo, TestServices.Default());
        var html = page.Render();

        // Bound group renders second → second occurrence of value="Team".
        var id = HandlerIn(html, "value=\"Team\"", "data-rask-on-change", skip: 1);
        await page.InvokeAsync(id, Value("true"));

        var html2 = page.Render();
        Assert.Contains("fc-radio-bound-out", html2);
        Assert.Contains("Plan: <strong>Team</strong>", html2);
    }

    // ---- BsCheckboxGroup ----

    [Fact]
    public async Task Checkbox_Controlled_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsCheckboxDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Interests: <strong>none</strong>", html);

        var id = HandlerIn(html, "value=\"AI\"", "data-rask-on-change");
        await page.InvokeAsync(id, Value("true"));

        Assert.Contains("Interests: <strong>AI</strong>", page.Render());
    }

    [Fact]
    public async Task Checkbox_Bound_OnChange_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsCheckboxDemo, TestServices.Default());
        var html = page.Render();

        var id = HandlerIn(html, "value=\"Mobile\"", "data-rask-on-change", skip: 1);
        await page.InvokeAsync(id, Value("true"));

        var html2 = page.Render();
        Assert.Contains("fc-checkbox-bound-out", html2);
        Assert.Contains("Interests: <strong>Mobile</strong>", html2);
    }

    // ---- BsMultiSelect ----

    [Fact]
    public async Task MultiSelect_Controlled_Select_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsMultiSelectDemo, TestServices.Default());
        var html = page.Render();
        Assert.Contains("Selected: <strong>none</strong>", html);

        // Option buttons in order (News, Sports, Tech, …); controlled group first → Tech is index 2.
        var clicks = ClickIds(html, "dropdown-item");
        await page.InvokeAsync(clicks[2]);

        Assert.Contains("Selected: <strong>Tech</strong>", page.Render());
    }

    [Fact]
    public async Task MultiSelect_Bound_Select_UpdatesReadout()
    {
        var page = RaskTest.Render(() => FormControlsMultiSelectDemo, TestServices.Default());
        var html = page.Render();

        // 5 controlled option buttons, then 5 bound → bound Tech is index 7.
        var clicks = ClickIds(html, "dropdown-item");
        await page.InvokeAsync(clicks[7]);

        var html2 = page.Render();
        Assert.Contains("fc-multiselect-bound-out", html2);
        Assert.Contains("Selected: <strong>Tech</strong>", html2);
    }

    // ---- helpers ----

    private static string Value(string v) => $"{{\"value\":\"{v}\"}}";

    // Returns the value of `attr` on the (skip-th) element tag that also contains `anchor`. Splitting on
    // '<' yields one element's attribute text per piece, so a match is scoped to a single tag.
    private static string HandlerIn(string html, string anchor, string attr, int skip = 0)
    {
        var marker = attr + "=\"";
        foreach (var tag in html.Split('<'))
        {
            if (!tag.Contains(anchor, StringComparison.Ordinal) ||
                !tag.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (skip-- > 0)
            {
                continue;
            }

            var s = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var e = tag.IndexOf('"', s);
            return tag.Substring(s, e - s);
        }

        throw new InvalidOperationException($"No '{attr}' handler on a tag containing '{anchor}'.");
    }

    // All data-rask-on-click ids on element tags carrying the given CSS class, in document order.
    private static List<string> ClickIds(string html, string cssClass)
    {
        var ids = new List<string>();
        const string marker = "data-rask-on-click=\"";
        foreach (var tag in html.Split('<'))
        {
            if (!tag.Contains(cssClass, StringComparison.Ordinal) ||
                !tag.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var s = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var e = tag.IndexOf('"', s);
            ids.Add(tag.Substring(s, e - s));
        }

        return ids;
    }
}
