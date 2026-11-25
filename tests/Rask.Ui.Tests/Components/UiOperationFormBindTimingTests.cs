using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A form over an operation, as a generated Form.For draws one: its fields with and without a timing of their own.</summary>
[Route("/ui-operation-form/timing")]
public sealed partial class OperationFormTimingPage : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            OperationForms.Of<TripInput>(
                "1",
                static _ => Task.FromResult<TripInput?>(new TripInput()),
                static (_, _) => Task.CompletedTask,
                static () => true)[f => [
                    Ui.Input.Bind(() => f.Name).Id("name").Label("Name").Validate(UiOperationFormBindTimingTests.AtLeastThree),
                    Ui.Textarea.Bind(() => f.Notes).Id("notes").Label("Notes"),
                    Ui.Input.Bind(() => f.Title).Id("title").Label("Title").Live(),
                    Ui.Input.Bind(() => f.Code).Id("code").Label("Code").Defer(),
                    Ui.Field.Bind(() => f.Place),
                    Ui.Field.Bind(() => f.Later).Defer(),
                ]]
        ];

    /// <summary>The operation's parameters.</summary>
    public sealed class TripInput
    {
        /// <summary>Checked as it is left.</summary>
        public string Name { get; set; } = "";

        /// <summary>A textarea with no timing of its own.</summary>
        public string Notes { get; set; } = "";

        /// <summary>Sent as it is typed.</summary>
        public string Title { get; set; } = "";

        /// <summary>Waits for the next action all the same.</summary>
        public string Code { get; set; } = "";

        /// <summary>A bound field with no timing of its own.</summary>
        public string Place { get; set; } = "";

        /// <summary>A bound field that waits for the next action all the same.</summary>
        public string Later { get; set; } = "";
    }
}

/// <summary>
///     A typed field in a form over an operation commits as it is left unless it says otherwise, so what the domain
///     refuses is told then; a hand form's fields still wait for the next action.
/// </summary>
public sealed partial class UiOperationFormBindTimingTests : global::Rask.Core.RaskMarkup
{
    internal static IEnumerable<string> AtLeastThree(string value) =>
        value.Length < 3 ? ["Name is too short."] : [];

    [Fact]
    public void A_field_under_Form_For_with_no_step_commits_as_it_is_left()
    {
        var page = Page.Visit("/ui-operation-form/timing");

        var name = page.Find("#name").Attribute("data-rask-bind-on");
        var notes = page.Find("#notes").Attribute("data-rask-bind-on");
        var place = page.Find("input[name=\"Place\"]").Attribute("data-rask-bind-on");

        Assert.Equal("blur", name);
        Assert.Equal("blur", notes);
        Assert.Equal("blur", place);
    }

    [Fact]
    public async Task A_field_under_Form_For_says_its_refusal_once_it_is_left_without_a_blur_step()
    {
        var page = Page.Visit("/ui-operation-form/timing");

        await page.Type("At").Into("Name");

        page.Shows("Name is too short.");
    }

    [Fact]
    public void An_explicit_live_step_under_Form_For_still_sends_as_it_is_typed()
    {
        var page = Page.Visit("/ui-operation-form/timing");

        var title = page.Find("#title");

        Assert.NotNull(title.Attribute("data-rask-on-input"));
        Assert.Equal("150", title.Attribute("data-rask-debounce"));
        Assert.Null(title.Attribute("data-rask-bind-on"));
    }

    [Fact]
    public void A_deferred_field_under_Form_For_waits_for_the_next_action()
    {
        var page = Page.Visit("/ui-operation-form/timing");

        var code = page.Find("#code").Attribute("data-rask-bind-on");
        var later = page.Find("input[name=\"Later\"]").Attribute("data-rask-bind-on");

        Assert.Equal("action", code);
        Assert.Equal("action", later);
    }

    [Fact]
    public async Task A_field_under_Form_Model_with_no_step_still_waits_for_the_next_action()
    {
        var m = new OperationFormTimingPage.TripInput();
        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Name).Label("Name").Validate(AtLeastThree)]);

        await page.Type("At").Into("Name");

        Assert.Equal("action", page.Find("input").Attribute("data-rask-bind-on"));
        page.DoesNotShow("Name is too short.");
        Assert.Equal("", m.Name);
    }
}
