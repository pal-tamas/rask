using Rask.Core.Components;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     Puts back the <c>&lt;body&gt;</c> a <c>Shell</c> override forgot, the way a browser's parser would (#1234).
/// </summary>
/// <remarks>
///     <para>
///         <c>Html[head, body]</c> reads right — the parameter is NAMED <c>body</c> — and paints right, because the
///         browser supplies a body itself. But the page is dead: the runtime <c>&lt;script&gt;</c> is written at
///         <c>&lt;/body&gt;</c>, the session id the runtime connects with is stamped on <c>&lt;body&gt;</c>, and a
///         live update ships the body, so with none of them written no socket opens and nothing says why.
///     </para>
///     <para>
///         Injecting the script alone would not revive it — the runtime would find no <c>data-rask-root</c> — so
///         the document gets the element instead: everything after the head goes into one <c>Body</c>, which is
///         where the browser puts it anyway, and a warning names the override so the developer fixes the source.
///     </para>
///     <para>
///         Decided on the tree <c>Shell</c> returned, before anything is serialized, and only when that tree
///         proves there is no body: a component of the app's own under <c>&lt;html&gt;</c> could write one when it
///         renders, so meeting one leaves the document as written, and the serializer reports it if it still
///         ends without a body (<see cref="ReportNoBodyWritten" />).
///     </para>
/// </remarks>
internal static class ShellBodyRepair
{
    private const string Category = "Rask.Shell";

    /// <summary>
    ///     <paramref name="document" />, with its content after the head wrapped in a <c>Body</c> when it is an
    ///     <c>&lt;html&gt;</c> element that places <paramref name="body" /> outside any.
    /// </summary>
    /// <param name="document">What the root's <c>Shell</c> returned; built for this render, so it is changed in place.</param>
    /// <param name="head">The framework's head, handed to <c>Shell</c>.</param>
    /// <param name="body">The app's content, handed to <c>Shell</c>.</param>
    /// <param name="app">The root component whose <c>Shell</c> it is, named in the warning.</param>
    internal static Component Ensure(Component document, Component head, Component body, Type app)
    {
        if (!string.Equals(document.TagNameInternal, "html", StringComparison.Ordinal)
            || document.Children is not IReadOnlyList<Component?> children
            || Place(children, head, body) != Placement.Bare)
        {
            return document;
        }

        document.Children = WrapAfterHead(children, head);
        RaskDiagnostics.ReportOnce(
            "shell-no-body:" + app.FullName,
            RaskLogLevel.Warning,
            Category,
            () => $"{app.FullName}.Shell places the app's content under <html> without a Body element, so Rask "
                  + "wrapped everything after the head in one. Without a <body> the live runtime <script> and the "
                  + "session id are never written, and the page cannot connect. The `body` parameter is the app's "
                  + "content, not the element: write Html[head, Body[body]].");
        return document;
    }

    /// <summary>Reports, once, a live document that closed its <c>&lt;html&gt;</c> without writing a body.</summary>
    internal static void ReportNoBodyWritten() =>
        RaskDiagnostics.ReportOnce(
            "shell-no-body:document",
            RaskLogLevel.Error,
            Category,
            static () => "The rendered document has no <body> element, so the live runtime <script> and the session "
                         + "id were not written and the page cannot connect: clicks and form submits do nothing. A "
                         + "root component's Shell override must place the app inside a Body element: "
                         + "Html[head, Body[body]].");

    private enum Placement
    {
        // The app's content is not in the tree (the override dropped it) — nothing to wrap.
        Absent,

        // A Body element is already there.
        InBody,

        // The app's content is there and no Body element is, as far as the static tree shows.
        Bare,

        // A component that renders later could write the body; the tree cannot say.
        Unknown,
    }

    private static Placement Place(IReadOnlyList<Component?> children, Component head, Component body)
    {
        var placement = Placement.Absent;
        foreach (var child in children)
        {
            var found = PlaceChild(child, head, body);
            if (found is Placement.InBody or Placement.Unknown)
            {
                return found;
            }

            if (found == Placement.Bare)
            {
                placement = Placement.Bare;
            }
        }

        return placement;
    }

    private static Placement PlaceChild(Component? child, Component head, Component body)
    {
        if (child is null or Text || ReferenceEquals(child, head))
        {
            return Placement.Absent;
        }

        if (ReferenceEquals(child, body))
        {
            return Placement.Bare;
        }

        if (string.Equals(child.TagNameInternal, "body", StringComparison.Ordinal))
        {
            return Placement.InBody;
        }

        // An element or a tagless group is markup already built, so its children can be read; anything else
        // (an app component, verbatim Raw markup, a lazy child sequence) is only known once it renders.
        var readable = child.TagNameInternal is not null || child is Fragment;
        return child.Children switch
        {
            _ when !readable => Placement.Unknown,
            null => Placement.Absent,
            IReadOnlyList<Component?> grandchildren => Place(grandchildren, head, body),
            _ => Placement.Unknown,
        };
    }

    // The head stays where Shell put it; everything else keeps its order inside one Body after it.
    private static Component?[] WrapAfterHead(IReadOnlyList<Component?> children, Component head)
    {
        var kept = new List<Component?>(2);
        var wrapped = new List<Component?>(children.Count);
        foreach (var child in children)
        {
            var isHead = child is not null
                         && (ReferenceEquals(child, head)
                             || string.Equals(child.TagNameInternal, "head", StringComparison.Ordinal));
            (isHead ? kept : wrapped).Add(child);
        }

        kept.Add(Markup.Body[wrapped]);
        return [.. kept];
    }
}
