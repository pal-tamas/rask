using Rask.Core.Forms;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     A bound kit field shows that an async validator is still checking it.
/// </summary>
/// <remarks>
///     <para>
///     A field whose validator goes to the network is otherwise silent for the length of the round trip, and a
///     reader who tabs away takes the silence for a pass. The state comes from the form's EditContext, so these
///     render a field against one with a check genuinely in flight rather than asserting on markup alone.
///     </para>
///     <para>
///     In the order a page sees it: the field renders first, THEN a check starts, then the page renders again —
///     a validator runs because someone typed into a field that is already on screen.
///     </para>
///     <para>
///     The assertions look for "Checking", not "Checking". Text is HTML-encoded on the way out, and the
///     ellipsis is not ASCII, so what reaches the page may be a character reference rather than the
///     character; the word itself is the contract.
///     </para>
/// </remarks>
public partial class UiFieldValidatingTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Name { get; set; } = "ada";
    }

    /// <summary>A validator whose check never finishes, so the field stays mid-validation for the render.</summary>
    private sealed class NeverCompletingAsyncValidator : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken ct) =>
            await new TaskCompletionSource().Task.ConfigureAwait(false);
    }

    /// <summary>The field's markup with a check in flight, rendered after the field is already on the page.</summary>
    private string RenderWhileChecking(bool showValidating = true)
    {
        var model = new Model();
        var ctx = new EditContext(model);
        ctx.AddValidator(new NeverCompletingAsyncValidator());

        var page = global::Rask.Testing.RaskTest.Render(() => Form.Model(model).Context(ctx)[
            showValidating
                ? UiInput.Bind(() => model.Name).Label("Name")
                : UiInput.Bind(() => model.Name).Label("Name").ShowValidating(false)
        ]);
        Assert.DoesNotContain("Checking", page.Html, StringComparison.Ordinal);

        var field = new FieldIdentifier(model, nameof(Model.Name));
        _ = ctx.ValidateFieldAsync(field);
        Assert.True(ctx.IsValidating(field));

        return page.Render();
    }

    [Fact]
    public void A_bound_field_shows_checking_while_its_validator_runs()
    {
        var html = RenderWhileChecking();

        // The words are what is announced; the spinner beside them is hidden from assistive tech.
        Assert.Contains("Checking", html, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("loading-spinner", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_idle_field_shows_no_checking_state()
    {
        var model = new Model();
        var ctx = new EditContext(model);

        var html = global::Rask.Testing.RaskTest.Render(() => Form.Model(model).Context(ctx)[
            UiInput.Bind(() => model.Name).Label("Name")
        ]).Html;

        Assert.DoesNotContain("Checking", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading-spinner", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowValidating_false_leaves_the_checking_state_to_the_call_site() =>
        Assert.DoesNotContain("Checking", RenderWhileChecking(showValidating: false), StringComparison.Ordinal);

    [Fact]
    public void The_checking_state_sits_after_the_control_and_inside_the_field()
    {
        var html = RenderWhileChecking();

        var fieldset = html.IndexOf("<div class=\"fieldset\">", StringComparison.Ordinal);
        var control = html.IndexOf("<input", StringComparison.Ordinal);
        var checking = html.IndexOf("Checking", StringComparison.Ordinal);

        Assert.True(fieldset >= 0 && fieldset < control && control < checking, html);
    }

    [Fact]
    public void A_controlled_field_has_no_checking_state_to_show()
    {
        // Validation state belongs to a bound field's EditContext entry; a controlled field has none.
        var html = global::Rask.Testing.RaskTest.Render(() => UiInput.Value("x").Label("Name")).Html;

        Assert.DoesNotContain("Checking", html, StringComparison.Ordinal);
    }
}
