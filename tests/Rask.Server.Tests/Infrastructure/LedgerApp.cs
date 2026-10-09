using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A list with a bin on every row, followed by a confirmation that is always on the page — the shape in which
///     removing a row moves every handler after it up by a row's worth.
/// </summary>
public sealed partial class LedgerApp : Component
{
    private sealed record Row(int Id);

    private List<Row> _rows = [new(1), new(2), new(3)];
    private Row? _removing;
    private string _removed = "";
    private int _closed;

    protected override Component? HeadAssets => Markup.Title["ledger"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"rows={string.Join(',', _rows.Select(row => row.Id))};removing={_removing?.Id};removed={_removed};closed={_closed};"],
        Div[_rows.Select(row => Div.Key(row.Id)[
            A.Href($"/rows/{row.Id}")["edit"],
            Button.Id($"bin{row.Id}").OnClick(() => _removing = row)["bin"]])],
        Div.Key("confirm-removal")[
            Button.Id("no").OnClick(() => { _removing = null; _closed++; })["No"],
            Button.Id("yes").OnClick(Remove)["Yes"]]
    ];

    private void Remove()
    {
        if (_removing is not { } row)
        {
            return;
        }

        _removed += row.Id;
        _rows = _rows.Where(other => other != row).ToList();
        _removing = null;
    }
}
