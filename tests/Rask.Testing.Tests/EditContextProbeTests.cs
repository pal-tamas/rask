using Rask.Core.Forms;

namespace Rask.Testing.Tests;

// Validation state (messages, IsModified, IsValidating) never reaches the markup, so without a way to reach
// the form's EditContext a consumer simply cannot assert it. These pin that the probe does.
public partial class EditContextProbeTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Probe_InsideAForm_CapturesTheAmbientContext()
    {
        EditContext? captured = null;
        var model = new Model();

        RaskTest.Render(() => Form(model)[
            Input(() => model.Name),
            RaskTest.EditContextProbe(c => captured = c)
        ]);

        Assert.NotNull(captured);
    }

    [Fact]
    public void Probe_RendersNoMarkupOfItsOwn()
    {
        var model = new Model();

        var page = RaskTest.Render(() => Form(model)[RaskTest.EditContextProbe(_ => { })]);
        var withoutProbe = RaskTest.Render(() => Form(model));

        Assert.Equal(withoutProbe.Html, page.Html);
    }

    [Fact]
    public async Task Probe_SeesStateTheMarkupNeverShows()
    {
        EditContext? captured = null;
        var model = new Model();

        var page = RaskTest.Render(() => Form(model)[
            Input(() => model.Name),
            RaskTest.EditContextProbe(c => captured = c)
        ]);

        var name = new FieldIdentifier(model, nameof(Model.Name));
        Assert.False(captured!.IsModified(name));

        await page.InputAsync("{\"value\":\"Ada\"}");

        // The point of the probe: this is a fact about the form that reading Html could never tell you.
        Assert.True(captured!.IsModified(name));
    }

    [Fact]
    public void Probe_OutsideAForm_CapturesNothing()
    {
        var captured = false;

        RaskTest.Render(() => Div[RaskTest.EditContextProbe(_ => captured = true)]);

        // There is no ambient context to hand over — placing the probe outside the form is a test bug, and
        // it stays silent rather than inventing a context.
        Assert.False(captured);
    }

    [Fact]
    public void Probe_NullCapture_Throws() =>
        Assert.Throws<ArgumentNullException>(() => RaskTest.EditContextProbe(null!));
}
