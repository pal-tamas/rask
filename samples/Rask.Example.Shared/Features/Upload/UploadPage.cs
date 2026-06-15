using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("upload")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UploadPage : Component
{
    protected override RenderResult Head => Title()["File upload — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "File upload",
            "Input(Type: \"file\", OnFiles: …) wires a file picker to a typed handler. RaskFile carries the metadata; OpenReadStream gives you a Stream for the bytes — over multipart POST on the server, via JS chunked reads on WASM."),
        H2(Class: "h4 mt-4 mb-3")["Pick a file"],
        CodeSample(
            EmbeddedSource.Read("UploadDemo.cs"),
            Notes:
            "The handler runs once per change event. RaskFile is only valid while the handler is on the stack — read whatever you need (bytes, metadata) before returning. The same component code runs unchanged on both hosts.",
            Result: UploadDemo())
    ];
}
