using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's File System Access API from Rask.Web — a tiny text editor: open a file from disk, edit it, and save it
///     <em>back to the same file</em> (or "Save as…" to a new one). Falls back to a notice where the pickers don't
///     exist (Firefox/Safari).
/// </summary>
public sealed partial class FileSystemAccessDemo : Component
{
    private Types.FileSystemFileHandle? _handle;
    private string? _name;
    private string _text = string.Empty;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Icon(Ui.IconName.FolderOpen).Id("fs-open").OnClick(Open)["Open file"],
                    Ui.Button.Icon(Ui.IconName.ArrowDownOnSquare)
                        .Id("fs-save")
                        .Disabled(_handle is null)
                        .OnClick(Save)["Save"],
                    Ui.Button.Id("fs-saveas").OnClick(SaveAs)["Save as…"]
                ],
                Div.Class("mb-2 text-sm text-ui-muted")["File: ", Code.Id("fs-name")[_name ?? "(none)"]],
                Ui.Textarea
                    .Value(_text)
                    .Label("File contents")
                    .Id("fs-text")
                    .Class("mb-2")
                    .Rows(8)
                    .Description("Open a text file, or type here and Save as…")
                    .OnInput(v => _text = v),
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("fs-status")[_status]]
            ];

    private const string Unsupported = "File System Access not supported — use Chrome/Edge";

    // window.showOpenFilePicker(): every file picked comes back kept; dismissing the picker rejects with AbortError.
    private async Task Open()
    {
        try
        {
            var picked = await Window.ShowOpenFilePicker(new()
            {
                Types =
                [
                    new()
                    {
                        Description = "Text files",
                        Accept = new(StringComparer.Ordinal) { ["text/plain"] = [".txt", ".md", ".json", ".cs"] }
                    }
                ]
            });
            await Adopt(picked[0]);
            await using var file = await picked[0].GetFile();
            _text = await file.Text();
            _status = $"Opened {_name} ({_text.Length} chars)";
        }
        catch (JSException ex) when (IsMissing(ex))
        {
            _status = Unsupported;
        }
        catch (JSException ex)
        {
            _status = "Open failed: " + ex.Message;
        }
    }

    private async Task Save()
    {
        if (_handle is null)
        {
            return;
        }

        try
        {
            await WriteText(_handle);
            _status = $"Saved {_name}";
        }
        catch (JSException ex)
        {
            _status = "Save failed: " + ex.Message;
        }
    }

    private async Task SaveAs()
    {
        try
        {
            var handle = await Window.ShowSaveFilePicker(new() { SuggestedName = "rask-note.txt" });
            await Adopt(handle);
            await WriteText(handle);
            _status = $"Saved to {_name}";
        }
        catch (JSException ex) when (IsMissing(ex))
        {
            _status = Unsupported;
        }
        catch (JSException ex)
        {
            _status = "Save failed: " + ex.Message;
        }
    }

    // A browser without the pickers has no window.showOpenFilePicker / showSaveFilePicker to call.
    private static bool IsMissing(JSException ex) => ex.Message.Contains("is not a function", StringComparison.Ordinal);

    private async Task WriteText(Types.FileSystemFileHandle handle)
    {
        await using var writable = await handle.CreateWritable();
        await writable.Write(_text);
        await writable.Close();
    }

    // Let the previous handle go before keeping a new one, so handles don't pile up across opens.
    private async Task Adopt(Types.FileSystemFileHandle handle)
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync();
        }

        _handle = handle;
        _name = await handle.Name;
    }

    protected override async Task OnUnmount()
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync();
        }
    }
}
