using System.Text;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IOriginPrivateFileSystem" /> — write a byte range into an app-owned file, read it back,
///     and ask for the origin's storage to survive eviction.
/// </summary>
public sealed class OriginPrivateFileSystemDemo(
    IOriginPrivateFileSystem fs,
    IStorageEstimator storage) : Component
{
    private const string Path = "demo/notes.bin";
    private const long Offset = 4096;

    private string? _content;
    private string? _size;
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "d-flex flex-wrap gap-2 mb-2")[
                    BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "opfs-write", OnClickAsync: Write)[
                        "Write at 4096"],
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "opfs-read", OnClickAsync: Read)[
                        "Read back"],
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "opfs-persist", OnClickAsync: Persist)[
                        "Request persistence"]
                ],
                Div(Class: "small text-secondary")["Content: ", Code(Id: "opfs-content")[_content ?? "(not read)"]],
                Div(Class: "small text-secondary")["File size: ", Code(Id: "opfs-size")[_size ?? "(unknown)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "opfs-status")[_status ?? "(idle)"]]
            ]
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
            await fs.WriteAsync(Path, Offset, Encoding.UTF8.GetBytes("hello from OPFS"));
            _size = await fs.GetSizeAsync(Path) is { } size ? $"{size} bytes" : "(missing)";
            _status = "Wrote 15 bytes at offset 4096";
        }
        catch (Exception ex) { _status = "Write failed: " + ex.Message; }
    }

    private async Task Read()
    {
        if (!await Supported())
        {
            return;
        }

        try
        {
            var bytes = await fs.ReadAsync(Path, Offset, 15);
            _content = bytes is null ? "(file does not exist)" : Encoding.UTF8.GetString(bytes);
            _status = "Read 15 bytes at offset 4096";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }

    // OPFS is persistent but still evictable under storage pressure until the origin is exempted.
    private async Task Persist()
    {
        try
        {
            var persisted = await storage.IsPersistedAsync() || await storage.RequestPersistAsync();
            _status = persisted
                ? "Storage is exempt from eviction"
                : "Storage is still evictable (declined or unsupported)";
        }
        catch (Exception ex) { _status = "Persist request failed: " + ex.Message; }
    }

    private async Task<bool> Supported()
    {
        if (await fs.IsSupportedAsync())
        {
            return true;
        }

        _status = "OPFS unavailable in this browser";
        return false;
    }
}
