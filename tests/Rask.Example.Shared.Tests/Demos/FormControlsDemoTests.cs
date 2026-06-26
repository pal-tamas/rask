using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The /form-controls showcase page demonstrates every control in both shapes — controlled (Value +
// OnChange) and bound (two-way Bind). These tests drive the live change/input/click handlers directly and
// assert the derived readout updates, proving the consumer re-renders for every (control × mode). The
// controlled cases are the regression guard for the controlled-OnChange dirty-mark fix; the bound cases
// pin two-way parity. The full browser walk is covered in SharedSmokeTests.
public sealed class FormControlsDemoTests
{
    // ---- Select ----

    [Fact]
    public async Task Select_Controlled_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsSelectDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Picked: <strong>Rask</strong>", html);

        var id = HandlerIn(html, "id=\"fc-select-controlled\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("Blazor"));

        Assert.Contains("Picked: <strong>Blazor</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Select_Bound_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsSelectDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        var id = HandlerIn(html, "id=\"fc-select-bound\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("htmx"));

        var html2 = host.RenderAsLiveRoot();
        Assert.Contains("fc-select-bound-out", html2);
        Assert.Contains("Picked: <strong>htmx</strong>", html2);
    }

    // ---- Input (text) ----

    [Fact]
    public async Task Input_Controlled_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsInputDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Echo: <strong>(empty)</strong>", html);

        var id = HandlerIn(html, "id=\"fc-input-controlled\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("hello"));

        Assert.Contains("Echo: <strong>hello</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Input_Bound_OnInput_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsInputDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        // A bound text Input streams via data-rask-on-input (per keystroke); the change handler only touches.
        var id = HandlerIn(html, "id=\"fc-input-bound\"", "data-rask-on-input");
        await host.TryInvokeHandlerAsync(id, Value("world"));

        Assert.Contains("Echo: <strong>world</strong>", host.RenderAsLiveRoot());
    }

    // ---- Textarea ----

    [Fact]
    public async Task Textarea_Controlled_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsTextareaDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Length: <strong>0</strong>", html);

        var id = HandlerIn(html, "id=\"fc-textarea-controlled\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("abcd"));

        Assert.Contains("Length: <strong>4</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Textarea_Bound_OnInput_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsTextareaDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        var id = HandlerIn(html, "id=\"fc-textarea-bound\"", "data-rask-on-input");
        await host.TryInvokeHandlerAsync(id, Value("abc"));

        var html2 = host.RenderAsLiveRoot();
        Assert.Contains("fc-textarea-bound-out", html2);
        Assert.Contains("Length: <strong>3</strong>", html2);
    }

    // ---- RadioGroup ----

    [Fact]
    public async Task Radio_Controlled_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsRadioDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Plan: <strong>Free</strong>", html);

        // Controlled group renders first → first occurrence of value="Pro".
        var id = HandlerIn(html, "value=\"Pro\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("true"));

        Assert.Contains("Plan: <strong>Pro</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Radio_Bound_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsRadioDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        // Bound group renders second → second occurrence of value="Team".
        var id = HandlerIn(html, "value=\"Team\"", "data-rask-on-change", skip: 1);
        await host.TryInvokeHandlerAsync(id, Value("true"));

        var html2 = host.RenderAsLiveRoot();
        Assert.Contains("fc-radio-bound-out", html2);
        Assert.Contains("Plan: <strong>Team</strong>", html2);
    }

    // ---- CheckboxGroup ----

    [Fact]
    public async Task Checkbox_Controlled_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsCheckboxDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Interests: <strong>none</strong>", html);

        var id = HandlerIn(html, "value=\"AI\"", "data-rask-on-change");
        await host.TryInvokeHandlerAsync(id, Value("true"));

        Assert.Contains("Interests: <strong>AI</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Checkbox_Bound_OnChange_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsCheckboxDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        var id = HandlerIn(html, "value=\"Mobile\"", "data-rask-on-change", skip: 1);
        await host.TryInvokeHandlerAsync(id, Value("true"));

        var html2 = host.RenderAsLiveRoot();
        Assert.Contains("fc-checkbox-bound-out", html2);
        Assert.Contains("Interests: <strong>Mobile</strong>", html2);
    }

    // ---- MultiSelect ----

    [Fact]
    public async Task MultiSelect_Controlled_Select_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsMultiSelectDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains("Selected: <strong>none</strong>", html);

        // Option buttons in order (News, Sports, Tech, …); controlled group first → Tech is index 2.
        var clicks = ClickIds(html, "dropdown-item");
        await host.TryInvokeHandlerAsync(clicks[2], Empty());

        Assert.Contains("Selected: <strong>Tech</strong>", host.RenderAsLiveRoot());
    }

    [Fact]
    public async Task MultiSelect_Bound_Select_UpdatesReadout()
    {
        var host = new LiveHost(() => FormControlsMultiSelectDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();

        // 5 controlled option buttons, then 5 bound → bound Tech is index 7.
        var clicks = ClickIds(html, "dropdown-item");
        await host.TryInvokeHandlerAsync(clicks[7], Empty());

        var html2 = host.RenderAsLiveRoot();
        Assert.Contains("fc-multiselect-bound-out", html2);
        Assert.Contains("Selected: <strong>Tech</strong>", html2);
    }

    // ---- helpers ----

    private static JsonElement Value(string v) =>
        JsonDocument.Parse($"{{\"value\":\"{v}\"}}").RootElement;

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;

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
