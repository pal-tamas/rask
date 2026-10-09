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
    public void An_unlabelled_control_is_still_the_bare_control() =>
        Assert.DoesNotContain(
            "<label",
            Ui.Otp.Value("").Length(6).ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void The_id_reaches_the_control_itself()
    {
        // A prop the base DECLARES and no control RENDERS is the silent failure this kit keeps meeting:
        // the call site compiles, the attribute never appears, and the browser test that selects on it
        // fails somewhere else entirely. Id was exactly that for one commit.
        Assert.Contains(
            "id=\"country\"",
            Ui.Otp.Value("").Length(6).Label("Country").Id("country").ToHtml(),
            StringComparison.Ordinal);

        // The two controls that are not UiFormFields and draw their own markup had no Id at all, which
        // left a browser test that clicks a checkbox or picks a file nothing to select on.
        Assert.Contains(
            "id=\"agree\"",
            Ui.Checkbox.Id("agree").Label("Agree").ToHtml(),
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"avatar\"",
            Ui.FileUpload.Label("Avatar").Id("avatar").ToHtml(),
            StringComparison.Ordinal);
    }
}
