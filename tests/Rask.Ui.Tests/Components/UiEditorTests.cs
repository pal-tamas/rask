using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's editor, as far as markup goes: the contract the engine (<c>Resources/editor/ui-editor.ts</c>)
///     finds its controls by, the first paint of the value, and what a prop writes. What the editor DOES is
///     the browser's to prove — <c>scripts/flux/parity-editor.mjs</c> and the site's E2E.
/// </summary>
public partial class UiEditorTests : global::Rask.Core.RaskMarkup
{
    private sealed class Post
    {
        public string? Body { get; set; }
    }

    [Fact]
    public void An_editor_is_a_marked_box_named_rich_text_editor_holding_a_toolbar_and_the_editable_area()
    {
        var html = Ui.Editor.ToHtml();

        var root = Regex.Match(html, "^<div [^>]*>").Value;
        Assert.Contains(" data-ui-editor", root);
        Assert.Contains(" data-ui-control", root);
        Assert.Contains(" aria-label=\"Rich text editor\"", root);
        Assert.Contains("role=\"toolbar\"", html);
        Assert.Contains("aria-label=\"Formatting\"", html);
        Assert.Contains("data-slot=\"content\" role=\"textbox\"", html);
    }

    [Fact]
    public void The_default_toolbar_is_heading_then_marks_then_lists_then_link_then_align()
    {
        var html = Ui.Editor.ToHtml();

        var items = Items(html);
        Assert.Equal(["heading", "bold", "italic", "strike", "bullet", "ordered", "blockquote", "link", "align"], items);
        Assert.Equal(4, Regex.Matches(html, "data-orientation=\"vertical\" role=\"none\"").Count);
    }

    [Fact]
    public void A_toolbar_list_names_its_items_with_a_bar_for_a_separator_and_a_tilde_for_the_spacer()
    {
        var html = Ui.Editor.Toolbar("heading | bold italic underline | align ~ undo redo").ToHtml();

        var items = Items(html);
        Assert.Equal(["heading", "bold", "italic", "underline", "align", "undo", "redo"], items);
        Assert.Equal(2, Regex.Matches(html, "data-orientation=\"vertical\" role=\"none\"").Count);
        Assert.Single(Regex.Matches(html, "<div class=\"flex-1\" role=\"none\">"));
    }

    [Fact]
    public void Every_documented_toolbar_item_has_a_name_in_the_list()
    {
        var html = Ui.Editor.Toolbar("subscript superscript highlight code").ToHtml();

        var items = Items(html);
        Assert.Equal(["subscript", "superscript", "highlight", "code"], items);
    }

    [Fact]
    public void An_unknown_toolbar_item_is_refused_with_the_items_there_are()
    {
        var editor = Ui.Editor.Toolbar("bold copy");

        var refused = Assert.Throws<ArgumentException>(() => editor.ToHtml());

        Assert.Contains("'copy' is not a toolbar item", refused.Message);
        Assert.Contains("Ui.EditorButton", refused.Message);
    }

    [Fact]
    public void The_value_is_the_first_paint_of_the_editable_area()
    {
        var html = Ui.Editor.Value("<h3>What's changed</h3><p>Shortcut keys</p>").ToHtml();

        var area = Regex.Match(html, "data-slot=\"content\" role=\"textbox\">(.*?)</div></div>").Groups[1].Value;
        Assert.Equal("<h3>What's changed</h3><p>Shortcut keys</p>", area);
    }

    [Fact]
    public void An_empty_editor_paints_the_empty_paragraph_that_carries_the_placeholder()
    {
        var html = Ui.Editor.Placeholder("Write something...").ToHtml();

        Assert.Matches(
            "<p class=\"is-empty is-editor-empty\" data-placeholder=\"Write something...\"><br class=\"ProseMirror-trailingBreak\" ?/?></p>",
            html);
        Assert.Contains(" data-placeholder=\"Write something...\"", Regex.Match(html, "^<div [^>]*>").Value);
    }

    [Fact]
    public void A_bound_editor_starts_from_the_html_its_model_holds()
    {
        var post = new Post { Body = "<p>Hello <strong>world</strong></p>" };

        var html = Ui.Editor.Bind(() => post.Body).ToHtml();

        Assert.Contains("role=\"textbox\"><p>Hello <strong>world</strong></p></div>", html);
    }

    [Fact]
    public void The_editable_area_is_the_one_subtree_rask_never_patches()
    {
        var html = Ui.Editor.Value("<p>x</p>").ToHtml();

        var host = Regex.Match(html, "<div data-rask-opaque><div [^>]*data-slot=\"content\"");
        Assert.True(host.Success, html);
        Assert.Single(Regex.Matches(html, "data-rask-opaque"));
    }

    [Fact]
    public void A_label_puts_the_editor_in_a_field_and_names_it()
    {
        var html = Ui.Editor.Label("Release notes").Description("Explain what's new in this release.").ToHtml();

        var root = Regex.Match(html, "<div [^>]*data-ui-editor[^>]*>").Value;
        Assert.StartsWith("<div class=", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-field", html);
        Assert.Contains(">Release notes</label>", html);
        Assert.Contains("aria-labelledby=\"f-release-notes-label\"", root);
        Assert.Contains("aria-describedby=\"f-release-notes-description\"", root);
    }

    [Fact]
    public void A_disabled_editor_says_so_and_disables_every_toolbar_button()
    {
        var html = Ui.Editor.Disabled(true).ToHtml();

        var toolbar = Regex.Match(html, "role=\"toolbar\".*?<div data-rask-opaque>", RegexOptions.Singleline).Value;
        var buttons = Regex.Matches(toolbar, "<button [^>]*tabindex=\"[^>]*>");
        Assert.Contains(" aria-disabled=\"true\"", Regex.Match(html, "^<div [^>]*>").Value);
        Assert.Equal(9, buttons.Count);
        Assert.All(buttons, button => Assert.Contains(" disabled", button.Value));
    }

    [Fact]
    public void An_invalid_editor_states_it_to_assistive_technology()
    {
        var html = Ui.Editor.Invalid(true).ToHtml();

        Assert.Contains(" aria-invalid=\"true\"", Regex.Match(html, "^<div [^>]*>").Value);
    }

    [Fact]
    public void A_toolbar_is_one_tab_stop_and_its_first_control_holds_it()
    {
        var html = Ui.Editor.ToHtml();

        var stops = Regex.Matches(Regex.Match(html, "role=\"toolbar\".*?<div data-rask-opaque>", RegexOptions.Singleline).Value, "<button [^>]*tabindex=\"(-?\\d)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();
        Assert.Equal("0", stops[0]);
        Assert.All(stops.Skip(1), stop => Assert.Equal("-1", stop));
    }

    [Fact]
    public void The_toolbar_controls_the_editable_area_by_its_id()
    {
        var html = Ui.Editor.Id("notes").ToHtml();

        Assert.Contains("aria-controls=\"notes-input\"", html);
        Assert.Contains("id=\"notes-input\"", html);
    }

    [Fact]
    public void Two_editors_nothing_names_do_not_share_an_id()
    {
        var html = Div[Ui.Editor, Ui.Editor].ToHtml();

        var ids = Regex.Matches(html, " id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_heading_item_is_a_combobox_over_a_listbox_of_text_and_three_levels()
    {
        var html = Ui.EditorHeading.ToHtml();

        var values = Regex.Matches(html, "role=\"option\"[^>]*data-value=\"(\\w+)\"|data-value=\"(\\w+)\"[^>]*role=\"option\"")
            .Select(match => match.Groups[1].Value + match.Groups[2].Value)
            .ToList();
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("aria-haspopup=\"listbox\"", html);
        Assert.Contains("popover=\"manual\"", html);
        Assert.Equal(["paragraph", "heading1", "heading2", "heading3"], values);
        Assert.Single(Regex.Matches(html, "aria-selected=\"true\""));
    }

    [Fact]
    public void The_link_item_holds_a_panel_with_the_address_and_its_two_buttons()
    {
        var html = Ui.EditorLink.ToHtml();

        Assert.Contains("data-ui-dropdown", html);
        Assert.Contains("data-ui-editor-link", html);
        Assert.Contains("data-editor=\"link:url\"", html);
        Assert.Contains("placeholder=\"https://...\"", html);
        Assert.Contains("data-editor=\"link:insert\"", html);
        Assert.Contains("data-editor=\"link:unlink\"", html);
    }

    [Fact]
    public void A_toggle_is_named_by_its_tooltip_and_shows_its_shortcut()
    {
        var html = Ui.EditorBold.ToHtml();

        var id = Regex.Match(html, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains("data-editor=\"bold\"", html);
        Assert.Contains($"id=\"{id}\"", html);
        Assert.Matches(">Bold <span class=\"ps-1 text-zinc-300\">[^<]+B</span></div>", html);
    }

    [Fact]
    public void A_button_of_your_own_draws_its_icon_under_a_tooltip()
    {
        var html = Ui.EditorButton.Icon(Ui.IconName.EllipsisHorizontal).Tooltip("More").ToHtml();

        Assert.Contains("data-ui-tooltip", html);
        Assert.Contains("data-ui-icon", html);
        Assert.Contains(">More</div>", html);
    }

    [Fact]
    public void A_composed_editor_keeps_the_parts_it_was_given()
    {
        var html = Ui.Editor[Ui.EditorToolbar[Ui.EditorBold, Ui.EditorSpacer, Ui.EditorUndo], Ui.EditorContent].ToHtml();

        var items = Items(html);
        Assert.Equal(["bold", "undo"], items);
        Assert.Contains("data-slot=\"content\"", html);
    }

    [Fact]
    public void The_engines_version_is_the_hash_of_the_committed_bundle()
    {
        var bundle = File.ReadAllBytes(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Resources", "ui-editor.js"));

        var hash = Convert.ToHexStringLower(SHA256.HashData(bundle))[..8];

        Assert.Equal(UiEditorEngine.Version, hash);
        Assert.Equal("/app/js/rask-ui-editor.js?v=" + hash, UiEditorEngine.Href("/app"));
    }

    [Fact]
    public void The_engine_travels_with_the_notices_of_what_it_bundles()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Resources", "ui-editor.LICENSES.txt"));

        var licences = Regex.Matches(notices, "^(\\S+) (\\S+) — (.+)$", RegexOptions.Multiline);
        Assert.Contains(licences, licence => licence.Groups[1].Value == "@tiptap/core" && licence.Groups[2].Value == "2.11.7");
        Assert.Contains(licences, licence => licence.Groups[1].Value == "prosemirror-model" && licence.Groups[2].Value == "1.25.1");
        Assert.All(licences, licence => Assert.Equal(licence.Groups[1].Value == "lucide" ? "ISC" : "MIT", licence.Groups[3].Value.Trim()));
    }

    [Fact]
    public void The_notices_carry_a_licence_text_for_every_package_and_for_the_icons_drawn_from_Lucide()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Resources", "ui-editor.LICENSES.txt"));

        var sections = Regex.Split(notices, "^={78}\n(?=\\S+ \\S+ — )", RegexOptions.Multiline).Skip(1).ToList();

        Assert.All(sections, section => Assert.Matches("Copyright", section));
        Assert.DoesNotContain("ships no licence file", notices);
        Assert.Contains("lucide 0.300.0 — ISC", notices);
        Assert.Contains("Lucide Contributors", notices);
        Assert.Contains("Copyright (c) 2025, Tiptap GmbH", notices);
    }

    [Fact]
    public void An_app_gets_the_engine_with_no_switch_and_a_library_gets_none()
    {
        var targets = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets"));
        var site = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Site", "Rask.Site.csproj"));

        var byDefault = Regex.Match(targets, "<RaskUiEditorEngine Condition=\"([^\"]+)\">true</RaskUiEditorEngine>").Groups[1].Value;

        // Flux's editor asks for no configuration: an app is recognised by its SDK, and nothing else turns it on.
        Assert.Contains("'$(UsingMicrosoftNETSdkWeb)' == 'true'", byDefault);
        Assert.Contains("'$(UsingMicrosoftNETSdkWebAssembly)' == 'true'", byDefault);
        Assert.DoesNotMatch("<RaskUiEditor>|\\$\\(RaskUiEditor\\)", targets);
        Assert.DoesNotContain("RaskUiEditor", site);
        // In this repository the targets reach every project, so the copy asks whether the kit is referenced.
        Assert.Contains("<Target Name=\"RaskUiWriteEditor\"\n          Condition=\"'@(_RaskUiEditorKit)' != ''\"", targets);
    }

    [Fact]
    public void The_bundle_names_the_notices_file_the_build_writes_beside_it()
    {
        var banner = File.ReadLines(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Resources", "ui-editor.js")).First();
        var targets = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets"));

        var named = Regex.Match(banner, "see (\\S+\\.LICENSES\\.txt)").Groups[1].Value;

        Assert.Equal("rask-ui-editor.LICENSES.txt", named);
        Assert.Contains("wwwroot/js/" + named, targets);
    }

    // The toolbar's controls, in order, by the name the engine finds each one by.
    private static List<string> Items(string html) =>
        Regex.Matches(Regex.Match(html, "role=\"toolbar\".*?<div data-rask-opaque>", RegexOptions.Singleline).Value, "data-editor=\"([a-z]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();
}
