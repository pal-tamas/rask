using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// A list with an editor open over it. Saving is the editor's handler, and what happens next is the page's:
// its callback closes the editor, then reloads with two reads that pass no token.
internal sealed partial class CallbackStubApp : Component
{
    private bool _editing = true;
    private int _rows;

    protected override Component? HeadAssets => Title["callback"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Span[_editing ? "editing" : $"edited:{_rows}"],
        _editing ? CallbackStubEditor.OnSaved(Saved) : null
    ];

    private async Task Saved()
    {
        _editing = false;
        _rows = await Read();
        _rows += await Read();
    }

    private static async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return 3;
    }
}

internal sealed partial class CallbackStubEditor : Component
{
    public Callback OnSaved { get; set; }

    protected override Component? Render() => Button.OnClick(Save)["save"];

    private async Task Save()
    {
        await Task.Delay(1);
        await OnSaved.Invoke();
    }
}
