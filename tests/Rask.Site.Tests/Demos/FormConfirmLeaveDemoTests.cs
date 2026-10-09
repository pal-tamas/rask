using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

/// <summary>
///     The forms guide's unsaved-changes demo renders a guarded form, and its save is what tells the browser the
///     form is clean.
/// </summary>
/// <remarks>
///     The browser half — the question, staying, the note kept — is the journey step on <c>#fcl-input</c> in
///     <c>SharedSmokeTests</c>. What a browser cannot say when that step fails is which half broke; this one
///     answers for the server's.
/// </remarks>
public sealed partial class FormConfirmLeaveDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_demo_form_carries_the_question_it_asks()
    {
        var page = Page.Render(() => FormConfirmLeaveDemo, TestServices.Default());

        var form = page.Find("#fcl-form");

        Assert.Equal("Leave without saving your note?", form.Attribute("data-rask-confirm-leave"));
        Assert.Null(form.Attribute("data-rask-saved"));
    }

    [Fact]
    public async Task Saving_the_note_marks_the_form_saved_and_shows_the_note()
    {
        var page = Page.Render(() => FormConfirmLeaveDemo, TestServices.Default());
        await page.On("#fcl-input").Change("remember the milk");

        await page.On("#fcl-form").Submit("{\"form\":{\"Note\":\"remember the milk\"}}");

        Assert.Equal("1", page.Find("#fcl-form").Attribute("data-rask-saved"));
        Assert.Contains("<strong>remember the milk</strong>", page.Html, StringComparison.Ordinal);
    }
}
