using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="IndexedDbDemo" /> (<c>IIndexedDb</c>).</summary>
[Route("browser/indexeddb")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class IndexedDbPage : Component
{
    protected override RenderResult Head => Title()["IndexedDB — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "IndexedDB",
            "A persistent, asynchronous key/value store backed by IndexedDB via IIndexedDb — much larger "
            + "than localStorage and non-blocking, for caching app data offline. Set a value and read it back; "
            + "it survives a reload. Works on both transports."),
        CodeSample(
            ["IndexedDbDemo.cs"],
            Notes: "OpenStoreAsync(name) returns an IKeyValueStore (SetAsync/GetAsync/DeleteAsync/KeysAsync/"
                + "ClearAsync) of string values — serialize objects to JSON. The full IndexedDB API "
                + "(indexes, cursors, schema migrations) is out of scope.",
            Result: IndexedDbDemo())
    ];
}
