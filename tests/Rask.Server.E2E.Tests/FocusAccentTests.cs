using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A focused field is ringed in the app's accent, inside its own box (owner, 2026-11-03): TA's quantity field in a
///     table row inside a flyout drew Safari's blue glow, cut in half by the table's scroll frame.
/// </summary>
public sealed class FocusAccentTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Field = "#qty-1";

    [Fact]
    public async Task A_field_in_a_table_row_in_a_flyout_is_ringed_in_the_accent_inside_its_own_box()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync(Field);

        await Expect(page.Locator(Field)).ToBeFocusedAsync();
        var drawn = await page.Locator(Field).EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); const i = document.createElement('i'); i.style.color = 'var(--color-accent)'; el.after(i);"
            + " const accent = getComputedStyle(i).color; i.remove(); return [s.outlineStyle, s.borderTopColor === accent ? 'accent' : s.borderTopColor, s.boxShadow.includes('inset') && s.boxShadow.includes(accent) ? 'inset accent' : s.boxShadow]; }");
        Assert.Equal(["none", "accent", "inset accent"], drawn);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task The_ringed_field_lies_wholly_inside_the_tables_scroll_frame_so_nothing_of_the_ring_is_cut()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.Keyboard.PressAsync("Tab");
        await page.FocusAsync(Field);

        var inside = await page.Locator(Field).EvaluateAsync<bool>(
            "el => { const f = el.closest('[data-ui-table]').parentElement.getBoundingClientRect(); const b = el.getBoundingClientRect();"
            + " return b.left >= f.left && b.top >= f.top && b.right <= f.right && b.bottom <= f.bottom; }");
        Assert.True(inside);
        Assert.True(await page.Locator(Field).EvaluateAsync<bool>("el => el.matches(':focus-visible')"));
        await Expect(page.Locator(Field)).ToHaveCSSAsync("outline-style", "none");
    }

    [Fact]
    public async Task A_button_draws_the_keyboard_ring_in_the_accent()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.Keyboard.PressAsync("Tab");
        await page.FocusAsync("#save");

        await Expect(page.Locator("#save")).ToHaveCSSAsync("outline-style", "solid");
        await Expect(page.Locator("#save")).ToHaveCSSAsync("outline-offset", "-2px");
    }

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<FlyoutLinesPage>(playwright);
        await session.HooksLoadedAsync();
        await session.Page.ClickAsync("#open");
        await Expect(session.Page.Locator(Field)).ToBeVisibleAsync();
        return session;
    }
}

/// <summary>TA's shape: a flyout holding a table whose first column is a field, at the scroll frame's very edge.</summary>
public sealed partial class FlyoutLinesPage : Component
{
    private bool _open;

    protected override Component? HeadAssets => [Markup.Title["a field in a table in a flyout"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() =>
    [
        Button.Id("open").OnClick(() => _open = true)["open"],
        Ui.Modal.Name("lines").Flyout().Open(_open).OnClose(() => _open = false)[
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn["Mennyiség"], Ui.TableColumn["Kút"]],
                Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell[Ui.Input.Value((id * 10).ToString(System.Globalization.CultureInfo.InvariantCulture)).Id("qty-" + id)],
                    Ui.TableCell["Kút " + id]
                ])]
            ],
            Ui.Button.Id("save")["Mentés"]
        ]
    ];
}
