using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IFileSystemAccess" /> — a tiny text editor: open a file from disk, edit it, and save it
///     <em>back to the same file</em> (or "Save as…" to a new one). Falls back to a notice where the API is
///     unsupported (Firefox/Safari).
/// </summary>
public sealed partial class FileSystemAccessDemo(IFileSystemAccess files) : Component, IAsyncDisposable
{
    private IFileHandle? _handle;
    private string _text = string.Empty;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Button.Class(Tw.BtnPrimary).Id("fs-open").OnClickAsync(Open)[
                        Icon.Name(IconName.Folder2Open).Class("me-1"), "Open file"],
                    Button
                        .Class(Tw.BtnOutlinePrimary)
                        .Id("fs-save")
                        .Disabled(_handle is null)
                        .OnClickAsync(Save)[Icon.Name(IconName.Save).Class("me-1"), "Save"],
                    Button.Class(Tw.BtnOutlinePrimary).Id("fs-saveas").OnClickAsync(SaveAs)[
                        "Save as…"]
                ],
                Div.Class("mb-2 text-sm text-slate-500 dark:text-slate-400")["File: ", Code.Id("fs-name")[_handle?.Name ?? "(none)"]],
                Textarea
                    .Value(_text)
                    .Id("fs-text")
                    .Class($"{Tw.Input} mb-2")
                    .Rows(8)
                    .Placeholder("Open a text file, or type here and Save as…")
                    .OnInput(v => _text = v),
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("fs-status")[_status]]
            ]
        ];

    private async Task Open()
    {
        try
        {
            if (!await files.IsSupportedAsync())
            {
                _status = "File System Access not supported — use Chrome/Edge";
                return;
            }

            var handle = await files.OpenFileAsync(new FilePickerOptions
            {
                Description = "Text files",
                Accept = new Dictionary<string, string[]> { ["text/plain"] = [".txt", ".md", ".json", ".cs"] }
            });
            if (handle is null)
            {
                _status = "Open cancelled";
                return;
            }

            await ReplaceHandle(handle);
            _text = await handle.ReadTextAsync();
            _status = $"Opened {handle.Name} ({_text.Length} chars)";
        }
        catch (Exception ex)
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
            await _handle.WriteTextAsync(_text);
            _status = $"Saved {_handle.Name}";
        }
        catch (Exception ex)
        {
            _status = "Save failed: " + ex.Message;
        }
    }

    private async Task SaveAs()
    {
        try
        {
            if (!await files.IsSupportedAsync())
            {
                _status = "File System Access not supported — use Chrome/Edge";
                return;
            }

            var handle = await files.SaveFileAsync(new SaveFilePickerOptions { SuggestedName = "rask-note.txt" });
            if (handle is null)
            {
                _status = "Save cancelled";
                return;
            }

            await ReplaceHandle(handle);
            await handle.WriteTextAsync(_text);
            _status = $"Saved to {handle.Name}";
        }
        catch (Exception ex)
        {
            _status = "Save failed: " + ex.Message;
        }
    }

    // Drop the previous JS-side handle before adopting a new one, so handles don't leak across opens.
    private async Task ReplaceHandle(IFileHandle handle)
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync();
        }

        _handle = handle;
    }

    public async ValueTask DisposeAsync()
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync();
        }
    }
}
