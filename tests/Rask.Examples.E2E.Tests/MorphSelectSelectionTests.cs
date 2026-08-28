using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     A full morph must move a <c>&lt;select&gt;</c>'s live selection, not just its <c>selected</c>
///     attributes (#596). Runs the production <c>rask-morph.js</c> + <c>rask-dom.js</c> in a real
///     browser against a hand-built incoming tree — no app, because the assertion is about DOM
///     semantics rather than about anything an app does.
/// </summary>
/// <remarks>
///     <para>
///         Why this cannot be a stub-DOM test like <c>MorphSelectedGuardTests</c>: the bug lives in the
///         HTML spec's option <em>dirtiness</em> flag. Once an option's selectedness has been set by the
///         user, or by the <c>.selected</c> setter the diff codec's <c>applySelected</c> uses, its
///         <c>selected</c> content attribute stops driving it — and a stub DOM that models attributes and
///         properties as independent fields reproduces neither half.
///     </para>
///     <para>
///         Worth recording, because it is the opposite of what it looks like: an attribute-only move is
///         <em>not</em> broken by any user interaction. Dirtiness blocks the attribute only on the option
///         that is dirty, so a move whose target is pristine still lands, and a single-select is rescued
///         again by "ask for a reset". The cases below are the ones that actually fail — a single-select
///         whose target the user has already touched, and multi-selects, which get no reset and so
///         accumulate selections no render ever asked for.
///     </para>
///     <para>
///         Frames that morph rather than diff: a reconnect, a scoped-CSS full reply, the WASM
///         full-document morph on boot and navigation, and the redeploy reload.
///     </para>
/// </remarks>
[Collection(BrowserOnlyCollection.Name)]
public sealed class MorphSelectSelectionTests(PlaywrightFixture playwright)
{
    // No whitespace between the options, deliberately. HtmlSerializer emits none, and a stray text node
    // between elements is a real difference to morph(): it pairs positionally, so the text node would be
    // matched against an <option>, replaced, and the trailing option dropped and re-created — which
    // resets the selection through the browser rather than through anything under test here.
    private const string SingleHtml = "<!doctype html><html><body><select id=\"s\">"
        + "<option value=\"a\">A</option><option value=\"b\" selected>B</option>"
        + "<option value=\"c\">C</option></select></body></html>";

    private const string MultiHtml = "<!doctype html><html><body><select id=\"s\" multiple>"
        + "<option value=\"a\" selected>A</option><option value=\"b\">B</option>"
        + "<option value=\"c\">C</option></select></body></html>";

    [Fact]
    public async Task A_morph_moves_a_single_select_even_onto_an_option_the_user_already_touched()
    {
        // The user lands on C having passed through A, so A is dirty. The server then says A. Before
        // the SELECT arm, the `selected` attribute could not move a dirty option and the box stayed
        // on C — the server's answer silently ignored.
        await using var page = await OpenAsync(SingleHtml);
        await page.SelectOptionAsync("#s", "a");
        await page.SelectOptionAsync("#s", "c");

        await MorphAsync(page, [0], multiple: false);

        Assert.Equal("a", await SelectionAsync(page));
    }

    [Fact]
    public async Task A_morph_deselects_a_multi_select_option_the_render_no_longer_marks()
    {
        // The sharpest case. The user picks B; the server renders A and C. The removeAttribute half of
        // the morph cannot clear B (dirty), so B survived on top of the incoming selection and the
        // control showed A, B and C — a state neither the user nor the server ever chose.
        await using var page = await OpenAsync(MultiHtml);
        await page.SelectOptionAsync("#s", new[] { "b" });

        await MorphAsync(page, [0, 2], multiple: true);

        Assert.Equal("a,c", await SelectionAsync(page));
    }

    [Fact]
    public async Task A_diff_applied_selection_does_not_stick_across_a_later_morph()
    {
        // Dirtiness is not only set by the user: syncFormProperty writes `.selected` directly, so any
        // select the diff codec has already moved is dirty from then on — which is how a reconnect
        // could show a selection the reconnecting server had long since changed.
        await using var page = await OpenAsync(MultiHtml);
        await page.EvaluateAsync("() => { document.getElementById('s').options[1].selected = true; }");

        await MorphAsync(page, [2], multiple: true);

        Assert.Equal("c", await SelectionAsync(page));
    }

    [Theory]
    [InlineData(false, new[] { 2 }, "c")]
    [InlineData(true, new[] { 1, 2 }, "b,c")]
    public async Task A_morph_still_applies_to_a_pristine_select(bool multiple, int[] marked, string want)
    {
        await using var page = await OpenAsync(multiple ? MultiHtml : SingleHtml);

        await MorphAsync(page, marked, multiple);

        Assert.Equal(want, await SelectionAsync(page));
    }

    [Fact]
    public async Task A_single_select_whose_render_marks_nothing_shows_its_first_option()
    {
        // What a fresh parse of the same markup shows. Writing selectedIndex -1 here would blank the
        // control, making a re-render look unlike a first load of the identical HTML.
        await using var page = await OpenAsync(SingleHtml);
        await page.SelectOptionAsync("#s", "c");

        await MorphAsync(page, [], multiple: false);

        Assert.Equal("a", await SelectionAsync(page));
    }

    [Fact]
    public async Task The_lagging_frame_guard_still_refuses_a_frame_that_predates_the_pick()
    {
        // The SELECT arm makes a full reply start moving selects it previously left alone, so #588's
        // guard has to be consulted here too — otherwise a reconnect would clobber a just-made pick,
        // trading one bug for its mirror image.
        await using var page = await OpenAsync(SingleHtml);
        await page.EvaluateAsync("() => window.__raskNote(document.getElementById('s'))");
        await page.SelectOptionAsync("#s", "c");

        await MorphAsync(page, [1], multiple: false);       // the pre-pick state — a lagging frame
        Assert.Equal("c", await SelectionAsync(page));

        await MorphAsync(page, [0], multiple: false);       // differs, so it is authoritative
        Assert.Equal("a", await SelectionAsync(page));
    }

    private async Task<IPage> OpenAsync(string html)
    {
        var page = await playwright.Browser.NewPageAsync();
        await page.SetContentAsync(html);

        // The real morph, bundled from BrowserFixtures/morph-select.ts by the build and published on
        // `window`. It used to be reached by reading rask-morph.js and rask-dom.js off disk and
        // evaluating them with `new Function(...)`, which was the only way in while those files were
        // bare declarations meant to be pasted into a host's scope.
        var bundle = Path.Combine(AppContext.BaseDirectory, "browser-fixtures", "morph-select.js");

        Assert.True(
            File.Exists(bundle),
            $"'{bundle}' is missing. It is bundled from BrowserFixtures/morph-select.ts by "
            + "_RaskBundleBrowserFixtures — build this project first.");

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = await File.ReadAllTextAsync(bundle) });

        return page;
    }

    // Build the incoming render as a detached tree and morph the live select against it — the shape a
    // full reply takes. The id is carried because morph removes any attribute the incoming tree lacks.
    private static Task MorphAsync(IPage page, int[] marked, bool multiple) =>
        page.EvaluateAsync(
            """
            ([marked, multiple]) => {
              const from = document.getElementById('s');
              const to = document.createElement('select');
              to.setAttribute('id', 's');
              if (multiple) to.setAttribute('multiple', '');
              ['a', 'b', 'c'].forEach((v, i) => {
                const o = document.createElement('option');
                o.setAttribute('value', v);
                o.textContent = v.toUpperCase();
                if (marked.includes(i)) o.setAttribute('selected', '');
                to.appendChild(o);
              });
              window.__raskMorph(from, to);
            }
            """,
            new object[] { marked, multiple });

    private static Task<string> SelectionAsync(IPage page) => page.EvalOnSelectorAsync<string>(
        "#s", "s => [...s.selectedOptions].map(o => o.value).join(',') || '(none)'");
}
