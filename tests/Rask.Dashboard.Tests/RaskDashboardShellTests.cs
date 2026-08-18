using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     <see cref="RaskDashboardShell" /> is the root a host with no components of its own renders the
///     dashboard through — the wasm-hosted template's <c>.Server</c> project, where the UI is a WASM SPA
///     and the ASP.NET host would otherwise have no <c>TApp</c> to name.
///     <para>
///         The shell is deliberately almost empty, and that is what makes it worth pinning: its whole job
///         is to render the router so the dashboard's route chain resolves beneath it, and to contribute
///         the two document-level head tags the layout cannot. If it stopped doing either, the wasm-hosted
///         dashboard would serve a blank or unscaled page with nothing failing anywhere else.
///     </para>
/// </summary>
public class RaskDashboardShellTests
{
    [Fact]
    public void Shell_EmitsADocument_WithTheDocumentLevelHeadTags()
    {
        var page = RaskTest.RenderDocument(new RaskDashboardShell()).Html;

        // Matched on the rendered tag rather than the source text: the head block keys its tags, so the
        // attribute is what survives into the document a browser receives.
        Assert.Contains("charset=\"utf-8\"", page, StringComparison.Ordinal);
        Assert.Contains("width=device-width", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The layout keeps ownership of the dashboard's chrome. Pinned here because the shell is the
    ///     tempting place to drift a second title or stylesheet link into, and a duplicate would only show
    ///     up as a subtly wrong <c>&lt;head&gt;</c> in one of the two templates.
    /// </summary>
    [Fact]
    public void Shell_DoesNotDuplicate_WhatTheLayoutOwns()
    {
        var page = RaskTest.RenderDocument(new RaskDashboardShell()).Html;

        Assert.DoesNotContain("noindex", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stylesheet", page, StringComparison.OrdinalIgnoreCase);
    }
}
