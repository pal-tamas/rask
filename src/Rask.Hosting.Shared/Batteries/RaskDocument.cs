using System.Reflection;
using Rask.Core.Live;

namespace Rask;

// The document defaults RaskApp and the WASM host hand the app's root — see RaskDocumentDefaults. Shared
// source, linked into both hosts, so the two cannot drift on what a page's <head> starts with.
internal static class RaskDocument
{
    // Written into the app's assembly by Rask.Tailwind when the project has Styles/app.css.
    internal const string StylesheetMetadata = "Rask.Stylesheet";

    public static RaskDocumentDefaults For(Assembly app, bool kit, ToastOptions? toasts = null) =>
        Defaults(app, kit, toasts ?? new ToastOptions());

    private static RaskDocumentDefaults Defaults(Assembly app, bool kit, ToastOptions toasts) =>
        new(
            kit ? UiStylesheet.Href : null,
            app.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, StylesheetMetadata, StringComparison.Ordinal))?.Value,
            kit
                ? new Dictionary<string, string?>(StringComparer.Ordinal) { [UiStylesheet.ThemeScopeAttribute] = "" }
                : null,
            // Toasts are always built in: the kit's look with the kit on, Rask's own without it.
            kit
                ? (messages, dismiss) => UiToaster.Messages(messages, dismiss, toasts)
                : (messages, dismiss) => Rask.Core.Components.DefaultToasts.Render(
                    messages, dismiss, toasts.Position == Ui.Position.Top, Align(toasts.Align)),
            toasts.Duration);

    private static string Align(Ui.Align align) => align switch
    {
        Ui.Align.Start => "start",
        Ui.Align.Center => "center",
        _ => "end",
    };
}
