namespace Rask.Core.Components;

// Where a server host serves the app's compiled stylesheet, and its content hash: what AppStylesheet links.
internal interface IAppStylesheetSource
{
    // Relative to the web root: css/app.css.
    string Path { get; }

    // For the link's ?v=; null when there is no sheet to hash.
    string? Hash { get; }
}
