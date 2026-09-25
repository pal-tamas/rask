using System.Reflection;
using Rask.Core.Live;

namespace Rask;

// The document defaults RaskApp and the WASM host hand the app's root — see RaskDocumentDefaults. Shared
// source, linked into both hosts, so the two cannot drift on what a page's <head> starts with.
internal static class RaskDocument
{
    // Written into the app's assembly by Rask.Tailwind when the project has Styles/app.css.
    internal const string StylesheetMetadata = "Rask.Stylesheet";

    public static RaskDocumentDefaults For(Assembly app, bool kit) =>
        new(
            kit ? UiStylesheet.Href : null,
            app.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == StylesheetMetadata)?.Value,
            kit ? new Dictionary<string, string?> { [UiStylesheet.ThemeScopeAttribute] = "" } : null);
}
