using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="FileSystemAccessDemo" /> (<c>IFileSystemAccess</c>).</summary>
[Route("browser/file-system")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class FileSystemAccessPage : Component
{
    protected override RenderResult Head => Title()["File system access — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "File system access",
            "Open a file from disk, edit it, and save it back to the same file — not just download a copy — "
            + "via IFileSystemAccess (the File System Access API). Powers in-browser editors and file "
            + "managers. Chromium-family only; gate on IsSupportedAsync and fall back to upload/download "
            + "elsewhere. The picker needs a user gesture."),
        CodeSample(
            ["FileSystemAccessDemo.cs"],
            Notes: "OpenFileAsync/SaveFileAsync return a disposable IFileHandle (null if cancelled); "
                + "ReadTextAsync/WriteTextAsync round-trip the file. The browser handle lives JS-side under a "
                + "minted id, so dispose the handle when done. OpenDirectoryAsync lists and opens entries.",
            Result: FileSystemAccessDemo())
    ];
}
