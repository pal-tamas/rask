using System.Text;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's origin private file system from Rask.Web — write a byte range into an app-owned file, read it back,
///     and ask for the origin's storage to survive eviction.
/// </summary>
public sealed partial class OriginPrivateFileSystemDemo : Component
{
    private const string Folder = "demo";
    private const string FileName = "notes.bin";
    private const long Offset = 4096;

    private string? _content;
    private string? _size;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex flex-wrap gap-2 mb-2")[
                    Ui.Button
                        .Id("opfs-write")
                        .OnClick(Write)["Write at 4096"],
                    Ui.Button
                        .Id("opfs-read")
                        .OnClick(Read)["Read back"],
                    Ui.Button
                        .Id("opfs-persist")
                        .OnClick(Persist)["Request persistence"]
                ],
                Div.Class("text-sm text-ui-muted")["Content: ", Code.Id("opfs-content")[_content ?? "(not read)"]],
                Div.Class("text-sm text-ui-muted")["File size: ", Code.Id("opfs-size")[_size ?? "(unknown)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("opfs-status")[_status ?? "(idle)"]]
            ];

    // Writing at an offset leaves everything outside the range intact and zero-fills the gap up to it, so
    // the file ends up larger than the bytes written — that's the point of a ranged write.
    private async Task Write()
    {
        if (!await Supported())
        {
            return;
        }

        try
        {
            await using var handle = await OpenFile(create: true);
            await using (var writable = await handle.CreateWritable(new() { KeepExistingData = true }))
            {
                await writable.Seek(Offset);
                await writable.Write(Encoding.UTF8.GetBytes("hello from OPFS"));
                await writable.Close();
            }

            await using var file = await handle.GetFile();
            _size = $"{await file.Size} bytes";
            _status = "Wrote 15 bytes at offset 4096";
        }
        catch (JSException ex) { _status = "Write failed: " + ex.Message; }
    }

    private async Task Read()
    {
        if (!await Supported())
        {
            return;
        }

        try
        {
            await using var handle = await OpenFile(create: false);
            await using var file = await handle.GetFile();
            _content = Encoding.UTF8.GetString(await file.Slice(Offset, Offset + 15).ArrayBuffer());
            _status = "Read 15 bytes at offset 4096";
        }
        catch (JSException ex)
        {
            _content = "(file does not exist)";
            _status = "Read failed: " + ex.Message;
        }
    }

    // navigator.storage.getDirectory() is the origin's root; without `create` a missing entry rejects (NotFoundError).
    private static async Task<Types.FileSystemFileHandle> OpenFile(bool create)
    {
        Types.FileSystemFileHandle file;
        await using (var root = await Navigator.Storage.GetDirectory())
        await using (var folder = await root.GetDirectoryHandle(Folder, new() { Create = create }))
        {
            file = await folder.GetFileHandle(FileName, new() { Create = create });
        }

        return file;
    }

    private async Task<bool> Supported()
    {
        if (await Navigator.Storage.IsSupported)
        {
            return true;
        }

        _status = "OPFS unavailable in this browser";
        return false;
    }

    // OPFS is persistent but still evictable under storage pressure until the origin is exempted.
    private async Task Persist()
    {
        try
        {
            var persisted = await Navigator.Storage.Persisted() || await Navigator.Storage.Persist();
            _status = persisted
                ? "Storage is exempt from eviction"
                : "Storage is still evictable (declined or unsupported)";
        }
        catch (JSException ex) { _status = "Persist request failed: " + ex.Message; }
    }
}
