namespace Rask.UiTests.Components;

/// <summary>
///     The field shape the daisyUI-drawn controls still share through <c>UiFormField&lt;T&gt;</c>: a label that
///     points at its control, a hint, and an id that reaches the element.
/// </summary>
/// <remarks>
///     The Flux-drawn controls — <c>Ui.Input</c>, <c>Ui.Textarea</c> — no longer derive from that base; what they
///     promise is in <c>UiInputTests</c>, <c>UiTextareaTests</c> and <c>UiFieldTests</c>. This file goes with the
///     base, when the last control on it is rebuilt.
/// </remarks>
public partial class UiFormFieldTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_select_gets_the_same_field_shape()
    {
        // The base is shared, so this is really asking whether Ui.Select reaches it — a control that
        // declared its own Label and kept it would pass every other test in this file while rendering the
        // old markup.
        // Options FIRST: it is a required prop, so the chain stays "pending" — and the optional steps
        // are not offered — until every required one is taken. Floating(false) because the sibling shape is
        // the legend's; the floating one is UiFloatingFieldTests'.
        var html = Ui.Select
            .Value("hu")
            .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
            .Label("Country")
            .Floating(false)
            .Hint("Where you are billed")
            .ToHtml();

        var label = html.IndexOf("<label", StringComparison.Ordinal);
        var close = html.IndexOf("</label>", StringComparison.Ordinal);
        var select = html.IndexOf("<select", StringComparison.Ordinal);

        Assert.True(label >= 0 && close >= 0 && select > close, "the select is inside its label.");
        Assert.Contains("for=", html, StringComparison.Ordinal);
        Assert.Contains("Country", html, StringComparison.Ordinal);
        Assert.Contains("Where you are billed", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_select_is_still_the_bare_control() =>
        Assert.DoesNotContain(
            "<label",
            Ui.Select.Value("hu").Options([("hu", "Hungary")]).ToHtml(),
            StringComparison.Ordinal);
    [Fact]
    public void The_id_reaches_the_control_itself()
    {
        // A prop the base DECLARES and no control RENDERS is the silent failure this kit keeps meeting:
        // the call site compiles, the attribute never appears, and the browser test that selects on it
        // fails somewhere else entirely. Id was exactly that for one commit.
        Assert.Contains(
            "id=\"country\"",
            Ui.Select.Value("hu").Options([("hu", "Hungary")]).Label("Country").Id("country").ToHtml(),
            StringComparison.Ordinal);

        // The two controls that are not UiFormFields and draw their own markup had no Id at all, which
        // left a browser test that clicks a checkbox or picks a file nothing to select on.
        Assert.Contains(
            "id=\"agree\"",
            Ui.Checkbox.Value(false).Id("agree")["Agree"].ToHtml(),
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"avatar\"",
            Ui.FileInput.Value("").Label("Avatar").Id("avatar").ToHtml(),
            StringComparison.Ordinal);
    }
}
