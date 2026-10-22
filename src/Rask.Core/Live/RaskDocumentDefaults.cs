namespace Rask.Core.Live;

// What a Rask-owned host writes into every document around the app, so App.cs does not have to: the
// charset and viewport, the UI kit's dark-mode script, the kit's stylesheet FIRST (it declares the @layer
// order for the whole document) and the app's compiled stylesheet. RaskApp and the WASM host
// register one and set it on the app's own root; a hand-wired MapRask host and a mounted app (the operator
// console) register none and build their own.
//
// Data, not markup: the root builds the tags (see RootErrorBoundary), so the hosts need no chain entries
// and Rask.Core needs no reference to the kit.
internal sealed class RaskDocumentDefaults(
    Func<string?, string>? kitStylesheet,
    string? appStylesheet,
    Func<Component>? headScript,
    Func<IReadOnlyList<Messaging.ToastMessage>, Action<int>, Component>? toasts = null,
    TimeSpan? toastDuration = null,
    bool toastsTimeThemselves = false,
    bool toastsKeepTheirPlace = false,
    Func<string?>? appStylesheetVersion = null)
{
    // The app sheet's content hash, asked at each document so a rebuilt sheet gets a new address; null where
    // the host does not serve the sheet itself (WASM), and then the link carries no ?v=.
    public Func<string?>? AppStylesheetVersion { get; } = appStylesheetVersion;

    // The template draws its host with no toast to show too, so a toast arriving is a change inside it (the kit's does).
    public bool ToastsKeepTheirPlace { get; } = toastsKeepTheirPlace;

    // The template writes each toast's countdown for the browser to run, so the outlet starts none (the kit's does).
    public bool ToastsTimeThemselves { get; } = toastsTimeThemselves;

    // How the session's toasts look when the app mounts no ToastOutlet of its own — the kit's, or Rask's own with the
    // kit off. Null only for a host that draws none (a mounted app keeps its own document).
    public Func<IReadOnlyList<Messaging.ToastMessage>, Action<int>, Component>? Toasts { get; } = toasts;

    // How long a toast shows unless it says otherwise (c.Toasts.For).
    public TimeSpan ToastDuration { get; } = toastDuration ?? TimeSpan.FromSeconds(5);

    // The kit sheet's href for a path base; null when the app turned the kit off.
    public Func<string?, string>? KitStylesheet { get; } = kitStylesheet;

    // The app's own compiled stylesheet, relative to wwwroot (Rask.Tailwind records it as
    // Rask.Stylesheet); null when the app has none.
    public string? AppStylesheet { get; } = appStylesheet;

    // A script the head starts with, ahead of every stylesheet — the kit's Ui.AppearanceScript; null without the kit.
    public Func<Component>? HeadScript { get; } = headScript;
}
