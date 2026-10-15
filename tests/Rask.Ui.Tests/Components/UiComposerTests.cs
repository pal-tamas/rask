using System.Text.RegularExpressions;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>Flux's composer: its markup, the keys it names for the runtime, its lines, its binding and its slots.</summary>
/// <remarks>The look is held to fluxui.dev by <c>ComposerParity</c>; the keys in a browser by <c>UiComposerHookTests</c>.</remarks>
public partial class UiComposerTests : global::Rask.Core.RaskMarkup
{
    private sealed class Chat
    {
        public string Prompt { get; set; } = "Hello";
    }

    [Fact]
    public void A_composer_is_a_group_around_a_textarea_two_lines_tall_that_grows_to_ten()
    {
        var composer = Ui.Composer.Of<string>().Placeholder("Ask");

        var html = composer.ToHtml();

        Assert.StartsWith("<div", html, StringComparison.Ordinal);
        Assert.Contains("role=\"group\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-composer", html, StringComparison.Ordinal);
        Assert.Contains("rows=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-grow=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Ask\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_or_Cmd_and_Enter_submits_unless_Enter_alone_is_asked_for()
    {
        var byDefault = Ui.Composer.Of<string>();
        var byEnter = Ui.Composer.Of<string>().Enter;

        var (plain, enter) = (byDefault.ToHtml(), byEnter.ToHtml());

        Assert.Contains("data-rask-submit-keys=\"Mod&#x2B;Enter\"", plain, StringComparison.Ordinal);
        Assert.Contains("data-rask-submit-keys=\"Enter\"", enter, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_and_max_rows_set_the_lines_and_max_rows_is_never_fewer_than_rows()
    {
        var composer = Ui.Composer.Of<string>().Rows(4).MaxRows(3);

        var html = composer.ToHtml();

        Assert.Contains("rows=\"4\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-grow=\"4\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sr_only_label_and_description_name_and_describe_the_group_out_of_sight()
    {
        var composer = Ui.Composer.Of<string>().Label("Prompt").LabelSrOnly().Description("Enter your prompt").DescriptionSrOnly();

        var html = composer.ToHtml();

        Assert.Contains("aria-labelledby=\"f-prompt-label\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-prompt-description\"", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("sr-only").Length - 1);
        Assert.Contains("id=\"f-prompt\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_composer_locks_its_textarea_and_every_button_in_its_slots()
    {
        var composer = Ui.Composer.Of<string>().Disabled()
            .ActionsLeading([Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip)])
            .ActionsTrailing(Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane));

        var html = composer.ToHtml();

        // The composer itself, as Flux writes it, its textarea and both buttons.
        Assert.Equal(4, Regex.Count(html, @"\sdisabled(?=[\s>])"));
    }

    [Fact]
    public void An_invalid_composer_marks_its_root()
    {
        var composer = Ui.Composer.Of<string>().Invalid();

        var html = composer.ToHtml();

        Assert.Contains("data-invalid", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_puts_the_leading_buttons_first_in_the_row()
    {
        var composer = Ui.Composer.Of<string>().Inline().ActionsLeading(Ui.Button.Sm.Ghost.Icon(Ui.IconName.Plus));

        var html = composer.ToHtml();

        Assert.Contains("col-start-1 row-start-1", html, StringComparison.Ordinal);
        Assert.Contains("col-start-2 col-span-2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_input_variant_keeps_the_buttons_own_corners()
    {
        var box = Ui.Composer.Of<string>();
        var input = Ui.Composer.Of<string>().Variant(Ui.ComposerVariant.Input);

        var (boxed, inputLike) = (box.ToHtml(), input.ToHtml());

        Assert.Contains("rounded-2xl", boxed, StringComparison.Ordinal);
        Assert.Contains("data-ui-button:rounded-lg", boxed, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-button:rounded-lg", inputLike, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_typed_into_a_bound_composer_reaches_the_model_with_the_submit()
    {
        var chat = new Chat();
        string? sent = null;
        var page = Page.Render(() => Form.Model(chat).OnSubmit(_ => sent = chat.Prompt)[
            Ui.Composer.Bind(() => chat.Prompt).Label("Prompt"),
            Ui.Button.Type(Ui.ButtonType.Submit)["Send"]
        ]);
        await page.Type("What is Rask?").Into("Prompt");

        await page.Click("Send");

        Assert.Equal("What is Rask?", sent);
    }

    [Fact]
    public void An_editor_in_the_input_slot_stands_where_the_textarea_was_without_a_box_of_its_own()
    {
        var chat = new Chat();

        var html = Ui.Composer.Bind(() => chat.Prompt).Rows(3).Placeholder("Ask")
            .Input(Ui.Editor.Toolbar("bold italic")).ToHtml();

        Assert.DoesNotContain("<textarea", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-editor", html, StringComparison.Ordinal);
        Assert.Contains("min-height:76px;max-height:216px", html, StringComparison.Ordinal);
        Assert.Contains("rounded-lg bg-zinc-100", html, StringComparison.Ordinal);
        Assert.DoesNotContain("shadow-xs dark:border-white/10 dark:bg-white/10 aria-disabled", html, StringComparison.Ordinal);
        Assert.Contains(">Hello</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_and_footer_slots_sit_above_and_below()
    {
        var composer = Ui.Composer.Of<string>().Header(Span["files"]).Footer(Span["note"]);

        var html = composer.ToHtml();

        Assert.True(html.IndexOf("files", StringComparison.Ordinal) < html.IndexOf("<textarea", StringComparison.Ordinal));
        Assert.True(html.IndexOf("note", StringComparison.Ordinal) > html.IndexOf("</textarea>", StringComparison.Ordinal));
    }
}
