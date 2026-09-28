namespace Rask.Server.Prerender;

/// <summary>How a page render ended.</summary>
internal enum PageRenderKind
{
    /// <summary>The page rendered a document.</summary>
    Rendered,

    /// <summary>The page navigated during its own render; the answer is a redirect, not a document.</summary>
    Redirect,
}
