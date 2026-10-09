using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An editor in a modal, in a real browser: saving closes the modal and the list behind it reloads with
///     the new row.
/// </summary>
/// <remarks>
///     The save is the editor's handler, and what follows is the page's <c>OnSaved</c>: it stops rendering the
///     editor, then reloads with two reads that pass no token. Those reads used to be cancelled with the editor
///     — the component the callback had just unmounted — so the list kept its old rows.
/// </remarks>
public sealed class ModalEditorSaveTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task Saving_in_a_modal_editor_closes_it_and_the_list_reloads_with_the_new_row()
    {
        await using var session = await HookSession.OpenAsync<ModalEditorJourneyPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#add");
        await page.FillAsync("#name", "Lisbon");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#rows li")).ToHaveTextAsync(["Porto", "Lisbon"]);
        await Expect(page.Locator("#count")).ToHaveTextAsync("total:2");
        await Expect(page.Locator("dialog")).ToBeHiddenAsync();
        await Expect(page.Locator("#name")).ToHaveCountAsync(0);
    }
}

internal sealed class ModalEditorRow
{
    public string Name { get; set; } = "";
}

/// <summary>A list, and an editor the page opens over it in a modal.</summary>
public sealed partial class ModalEditorJourneyPage : Component
{
    private readonly List<string> _table = ["Porto"];
    private IReadOnlyList<string> _rows = ["Porto"];
    private int _total = 1;
    private bool _editing;

    protected override Component? HeadAssets => Markup.Title["modal editor"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("add").OnClick(() => { _editing = true; })["add"],
        Ul.Id("rows")[_rows.Select(row => Li.Key(row)[row])],
        P.Id("count")[$"total:{_total}"],
        Ui.Modal.Open(_editing)[_editing ? ModalEditorJourneyEditor.OnSaved(Saved) : null]
    ];

    private async Task Saved(string name)
    {
        _table.Add(name);
        _editing = false;
        await Reload();
    }

    private async Task Reload()
    {
        _rows = await Read(() => _table.ToList());
        _total = await Read(() => _table.Count);
    }

    // A read as a data call makes it: no token passed, so it is cancelled with the work in progress.
    private static async Task<T> Read<T>(Func<T> query)
    {
        var token = Current.Cancellation;
        await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return query();
    }
}

public sealed partial class ModalEditorJourneyEditor : Component
{
    private readonly ModalEditorRow _row = new();

    public Callback<string> OnSaved { get; set; }

    protected override Component? Render() =>
        Form.Model(_row).OnSubmit(Save)[
            Input.Bind(() => _row.Name).Id("name"),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ];

    private async Task Save(ModalEditorRow row)
    {
        await Task.Delay(20);
        await OnSaved.Invoke(row.Name);
    }
}
