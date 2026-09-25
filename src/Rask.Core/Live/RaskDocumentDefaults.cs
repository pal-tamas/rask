namespace Rask.Core.Live;

// What a Rask-owned host writes into every document around the app, so App.cs does not have to: the
// charset and viewport, the UI kit's stylesheet FIRST (it declares the @layer order for the whole
// document), the app's compiled stylesheet, and the kit's theme scope on <html>. RaskApp and the WASM host
// register one and set it on the app's own root; a hand-wired MapRask host and a mounted app (the operator
// console) register none and build their own.
//
// Data, not markup: the root builds the tags (see RootErrorBoundary), so the hosts need no chain entries
// and Rask.Core needs no reference to the kit.
internal sealed class RaskDocumentDefaults(
    Func<string?, string>? kitStylesheet,
    string? appStylesheet,
    IReadOnlyDictionary<string, string?>? htmlAttributes)
{
    // The kit sheet's href for a path base; null when the app turned the kit off.
    public Func<string?, string>? KitStylesheet { get; } = kitStylesheet;

    // The app's own compiled stylesheet, relative to wwwroot (Rask.Tailwind records it as
    // Rask.Stylesheet); null when the app has none.
    public string? AppStylesheet { get; } = appStylesheet;

    // Read by Component's default Shell. An app that overrides Shell writes its own <html> and skips these.
    public IReadOnlyDictionary<string, string?>? HtmlAttributes { get; } = htmlAttributes;
}
