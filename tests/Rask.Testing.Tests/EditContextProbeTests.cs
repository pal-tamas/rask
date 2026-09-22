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
    public void A_probe_inside_a_form_captures_the_ambient_context()
    {
        EditContext? captured = null;
        var model = new Model();

        Test.Render(() => Form.Model(model)[
            Input.Bind(() => model.Name),
            Test.EditContextProbe(c => captured = c)
        ]);

        Assert.NotNull(captured);
    }

    [Fact]
    public void The_probe_renders_no_markup_of_its_own()
    {
        var model = new Model();

        var page = Test.Render(() => Form.Model(model)[Test.EditContextProbe(_ => { })]);
        var withoutProbe = Test.Render(() => Form.Model(model));

        Assert.Equal(withoutProbe.Html, page.Html);
    }

    [Fact]
    public async Task The_probe_sees_state_the_markup_never_shows()
    {
        EditContext? captured = null;
        var model = new Model();

        var page = Test.Render(() => Form.Model(model)[
            Input.Bind(() => model.Name),
            Test.EditContextProbe(c => captured = c)
        ]);
        var name = new FieldIdentifier(model, nameof(Model.Name));

        Assert.False(captured!.IsModified(name));

        await page.InputAsync("{\"value\":\"Ada\"}");

        // The point of the probe: this is a fact about the form that reading Html could never tell you.
        Assert.True(captured!.IsModified(name));
    }

    [Fact]
    public void A_probe_outside_a_form_captures_nothing()
    {
        var captured = false;

        Test.Render(() => Div[Test.EditContextProbe(_ => captured = true)]);

        // There is no ambient context to hand over — placing the probe outside the form is a test bug, and
        // it stays silent rather than inventing a context.
        Assert.False(captured);
    }

    [Fact]
    public void A_probe_with_a_null_capture_throws() =>
        Assert.Throws<ArgumentNullException>(() => Test.EditContextProbe(null!));
}
