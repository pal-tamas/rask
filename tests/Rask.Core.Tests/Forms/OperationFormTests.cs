using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the roots it renders; there is no parent to build them

namespace Rask.Core.Tests.Forms;

// F-43: the form a generated Form.For hands back — read before it is drawn, its fields over what was read, a stand-in
// for an id no row has, and every control disabled for a principal the operation's roles do not admit.
public partial class OperationFormTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task The_form_is_drawn_once_the_aggregate_is_read_with_its_fields_filled_from_it()
    {
        var host = new RenameHost { Read = () => new RenameInput { Name = "Trailer 7" } };

        var html = await Prerendered(host);

        Assert.Contains("value=\"Trailer 7\"", html, StringComparison.Ordinal);
        Assert.Contains("<form", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<fieldset", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_id_no_row_has_draws_the_stand_in_and_no_form()
    {
        var host = new RenameHost { Read = () => null };

        var html = await Prerendered(host);

        Assert.Contains("No such trailer.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_principal_the_roles_do_not_admit_sees_every_control_disabled()
    {
        var host = new RenameHost { Read = () => new RenameInput { Name = "Trailer 7" }, May = false };

        var html = await Prerendered(host);

        Assert.Contains("<fieldset disabled", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Trailer 7\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_form_takes_the_steps_a_hand_written_form_does()
    {
        var host = new RenameHost { Read = () => new RenameInput { Name = "Trailer 7" }, Shaped = true };

        var html = await Prerendered(host);

        Assert.Contains("<form id=\"rename\" class=\"grid gap-4\" style=\"max-width: 40rem\" data-kind=\"trailer\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"off\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-leave=\"Leave without saving?\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_form_with_no_host_toasts_its_own_message()
    {
        var host = new RenameHost { Read = () => new RenameInput { Name = "Trailer 7" }, Shaped = true, Yields = false };
        using var toasts = Toast.Fake();
        var page = global::Rask.Testing.Page.Render(() => host);

        await page.On("form").Submit();

        toasts.Shown("Trailer renamed.").Once();
    }

    [Fact]
    public async Task A_submit_its_own_validation_refuses_runs_the_invalid_handler_and_saves_nothing()
    {
        var host = new RenameHost { Read = () => new RenameInput { Name = "" }, Shaped = true, Yields = false };
        using var toasts = Toast.Fake();
        var page = global::Rask.Testing.Page.Render(() => host);

        await page.On("form").Submit();

        Assert.Equal(1, host.Refused);
        toasts.Shown("Trailer renamed.").Never();
    }

    private static async Task<string> Prerendered(Component host)
    {
        var result = await RaskPrerender.RenderDocument(
            host, RenderHarness.EmptyServices(), WaitFor.HangCeiling, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.TimedOut);
        return result.Html;
    }

    public sealed class RenameInput
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; set; } = "";
    }

    private sealed class RenameHost : Component
    {
        public required Func<RenameInput?> Read { get; init; }

        public bool May { get; init; } = true;

        public bool Shaped { get; init; }

        public bool Yields { get; init; } = true;

        public int Refused { get; private set; }

        protected override Component? Render()
        {
            var form = OperationForms.Of<RenameInput>(
                7,
                async _ =>
                {
                    if (Yields)
                    {
                        await Task.Yield();
                    }

                    return Read();
                },
                static (_, _) => Task.CompletedTask,
                () => May)
            .Missing(P["No such trailer."]);
            if (Shaped)
            {
                form = form.Id("rename").Class("grid", null, "gap-4").Style("max-width: 40rem").Data("kind", "trailer")
                    .Attributes("autocomplete", "off").ConfirmLeave("Leave without saving?").Saved("Trailer renamed.")
                    .OnInvalidSubmit(_ =>
                    {
                        Refused++;
                        return Task.CompletedTask;
                    });
            }

            return form[f => [
                Input.Bind(() => f.Name).Id("name"),
                Button["Rename"],
            ]];
        }
    }
}
