using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("upload")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UploadPage : Component
{
    private string? _contentType;
    private DateTimeOffset _modified;
    private string? _name;
    private long _size;

    protected override RenderResult Head => Title()["File upload — Rask"];

    private void OnFiles(IReadOnlyList<RaskFile> files)
    {
        if (files.Count == 0)
        {
            _name = null;
            return;
        }

        var file = files[0];
        _name = file.Name;
        _size = file.Size;
        _contentType = file.ContentType;
        _modified = file.LastModified;
    }

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "File upload",
            "Input(Type: \"file\", OnFiles: …) wires a file picker to a typed handler. RaskFile carries the metadata; OpenReadStream gives you a Stream for the bytes — over multipart POST on the server, via JS chunked reads on WASM."),
        H2(Class: "h4 mt-4 mb-3")["Pick a file"],
        CodeSample(
            """
            Input(Type: "file", OnFiles: files => {
                var file = files[0];
                _name = file.Name;
                _size = file.Size;
                _contentType = file.ContentType;
                _modified = file.LastModified;
            })

            // Inside the same handler — or any handler that ran while the
            // file is still alive — read the bytes:
            // using var s = file.OpenReadStream(maxAllowedSize, CancellationToken);
            // await s.CopyToAsync(destination);
            """,
            Notes:
            "The handler runs once per change event. RaskFile is only valid while the handler is on the stack — read whatever you need (bytes, metadata) before returning. The same component code runs unchanged on both hosts.",
            Result: RenderResult())
    ];

    private Component RenderResult() =>
        Div()[
            Input(
                Id: "upload-input",
                Type: "file",
                Class: "form-control mb-3",
                OnFiles: OnFiles),
            _name is null
                ? (Component)Div(Class: "text-secondary small")["No file selected yet."]
                : Dl(Class: "row small mb-0")[
                    Dt(Class: "col-4 text-secondary")["Name"],
                    Dd(Class: "col-8 text-break", Data: Meta("name"))[_name],
                    Dt(Class: "col-4 text-secondary")["Size"],
                    Dd(Class: "col-8", Data: Meta("size"))[_size.ToString("N0", CultureInfo.InvariantCulture),
                        " bytes"],
                    Dt(Class: "col-4 text-secondary")["Type"],
                    Dd(Class: "col-8", Data: Meta("type"))[_contentType ?? string.Empty],
                    Dt(Class: "col-4 text-secondary")["Modified"],
                    Dd(Class: "col-8 mb-0", Data: Meta("modified"))[
                        _modified.ToString("u", CultureInfo.InvariantCulture)]
                ]
        ];

    private static IReadOnlyDictionary<string, string?> Meta(string field) =>
        new Dictionary<string, string?> { ["rask-meta"] = field };
}
