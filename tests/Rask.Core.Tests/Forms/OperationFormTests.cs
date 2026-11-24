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

    private static async Task<string> Prerendered(Component host)
    {
        var result = await RaskPrerender.RenderDocument(
            host, RenderHarness.EmptyServices(), WaitFor.HangCeiling, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.TimedOut);
        return result.Html;
    }

    public sealed class RenameInput
    {
        public string Name { get; set; } = "";
    }

    private sealed class RenameHost : Component
    {
        public required Func<RenameInput?> Read { get; init; }

        public bool May { get; init; } = true;

        protected override Component? Render() =>
            OperationForms.Of<RenameInput>(
                7,
                async _ =>
                {
                    await Task.Yield();
                    return Read();
                },
                static (_, _) => Task.CompletedTask,
                () => May)
            .Missing(P["No such trailer."])[f => [
                Input.Bind(() => f.Name).Id("name"),
                Button["Rename"],
            ]];
    }
}
