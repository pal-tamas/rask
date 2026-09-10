using Rask.Site.Tests.Infrastructure;
using Rask.Ui;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     A bound kit field writes what was typed back to the model — inside a <c>Form</c> and outside one.
/// </summary>
/// <remarks>
///     <para>
///     The kit's own binding tests assert the rendered value only, so every one of them reads in ONE
///     direction: a control that read the model perfectly and wrote nowhere passes the whole suite. That
///     gap is why this exists, and it is in the SITE's suite because this is where the working harness
///     lives — <c>TestServices.Default()</c> plus <c>InvokeAsync</c>, which is what the other bound-control
///     tests here already drive.
///     </para>
///     <para>
///     Two arrangements, because they are not the same question. A bound control outside a form binds
///     straight to the expression; inside a form it also has to reach the ambient <c>EditContext</c>, and
///     the showcase only ever exercised the first through the kit.
///     </para>
/// </remarks>
public sealed partial class KitFieldWriteBackTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    private sealed partial class BareHost : Component
    {
        public Model Data { get; set; } = new();

        protected override Component? Render() => UiInput.Bind(() => Data.Name).Label("Name");
    }

    private sealed partial class RawHost : Component
    {
        public Model Data { get; set; } = new();

        protected override Component? Render() => Input.Bind(() => Data.Name);
    }

    private sealed partial class FormHost : Component
    {
        public Model Data { get; set; } = new();

        protected override Component? Render() =>
            Form.Model(Data)[UiInput.Bind(() => Data.Name).Label("Name")];
    }

    [Fact]
    public async Task The_raw_element_writes_back_in_this_harness()
    {
        // The control the kit forwards to, in the same host and the same harness. This is the guard on the
        // two tests below meaning anything: if it fails, the harness is wrong rather than the kit.
        var host = new RawHost();
        var page = RaskTest.Render(host, TestServices.Default());

        var id = Handler(page.Render(), "data-rask-on-input");
        await page.InvokeAsync(id, "{\"value\":\"hopper\"}");

        Assert.Equal("hopper", host.Data.Name);
    }

    [Fact]
    public async Task Outside_a_form_a_bound_field_writes_back()
    {
        var host = new BareHost();
        var page = RaskTest.Render(host, TestServices.Default());

        var id = Handler(page.Render(), "data-rask-on-input");
        await page.InvokeAsync(id, "{\"value\":\"ada\"}");

        Assert.Equal("ada", host.Data.Name);
    }

    [Fact]
    public async Task Inside_a_form_a_bound_field_writes_back()
    {
        var host = new FormHost();
        var page = RaskTest.Render(host, TestServices.Default());

        var id = Handler(page.Render(), "data-rask-on-input");
        await page.InvokeAsync(id, "{\"value\":\"grace\"}");

        Assert.Equal("grace", host.Data.Name);
    }

    private static string Handler(string html, string attr)
    {
        var marker = attr + "=\"";

        foreach (var tag in html.Split('<'))
        {
            if (!tag.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var s = tag.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return tag[s..tag.IndexOf('"', s)];
        }

        throw new InvalidOperationException($"nothing in the markup carries '{attr}'.");
    }
}
