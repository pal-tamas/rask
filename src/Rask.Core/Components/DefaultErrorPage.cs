using System.Diagnostics;
using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

[SkipFactory]
public sealed class DefaultErrorPage : Component
{
    // Inline style constants — Core takes no styling dependency, so the page carries its own.
    private const string TypeStyle =
        "margin:0 0 0.5rem;color:#4b5563;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:0.9rem;";

    private const string MessageStyle =
        "margin:0;padding:0.75rem;background:#fbe9e9;border-radius:0.375rem;white-space:pre-wrap;"
        + "font-size:0.9rem;color:#7f1d1d;";

    private const string FrameStyle =
        "margin:0.5rem 0 0;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:0.78rem;color:#4b5563;"
        + "white-space:pre-wrap;word-break:break-word;";

    private const string ExcerptStyle =
        "margin:0.25rem 0 0;padding:0.5rem 0.75rem;background:#f3f4f6;border-radius:0.375rem;"
        + "white-space:pre;overflow:auto;font-size:0.75rem;color:#4b5563;line-height:1.5;";

    private const string CausedByStyle =
        "cursor:pointer;margin-top:1rem;font-size:0.85rem;color:#7f1d1d;font-weight:600;";

    private const string ReloadButtonStyle =
        "margin:0.25rem 0 1rem;padding:0.5rem 1rem;border:1px solid #b42323;border-radius:0.375rem;"
        + "background:#b42323;color:#fff;font:inherit;font-size:0.9rem;cursor:pointer;";

    // How many source lines to show on each side of the throwing line.
    private const int SourceRadius = 5;

    // Guards against a pathological/cyclic InnerException chain.
    private const int MaxChainDepth = 20;

    private readonly Exception _error;
    private readonly bool _isDevelopment;

    private readonly Callback? _recover;

    public DefaultErrorPage(Exception error) : this(error, IsDevelopmentEnvironment())
    {
    }

    /// <summary>
    ///     The page with an in-session recovery action. <paramref name="recover" /> is the boundary's
    ///     <c>Recover</c>, which clears the error and re-renders the subtree.
    /// </summary>
    /// <remarks>
    ///     Worth having because the common fault is a handler that threw, not a render that cannot
    ///     succeed: the tree is intact, so clearing the error puts the app straight back with its state
    ///     and scroll position, where the reload button costs a full round trip and all of it. A render
    ///     that faults deterministically simply throws again and lands back here, which is the honest
    ///     outcome and is what React's boundary does too.
    /// </remarks>
    public DefaultErrorPage(Exception error, Callback recover)
        : this(error, IsDevelopmentEnvironment(), recover)
    {
    }

    // Test seam: construct with an explicit environment so unit tests need no process-global env var
    // (setting ASPNETCORE_ENVIRONMENT would race other tests that render this page).
    internal DefaultErrorPage(Exception error, bool isDevelopment, Callback? recover = null)
    {
        _error = error;
        _isDevelopment = isDevelopment;
        _recover = recover;
    }

    protected override bool BypassRenderCache => true;

    /// <summary>
    ///     Whether this page stands in for the <b>whole</b> app rather than one failed subtree — set only
    ///     by the root boundary, and the condition on <see cref="Head" /> below.
    /// </summary>
    internal bool OwnsDocument { get; init; }

    /// <summary>
    ///     What this page needs in <c>&lt;head&gt;</c> to stand on its own — but only when it <em>is</em>
    ///     the page.
    /// </summary>
    /// <remarks>
    ///     The framework owns the document, so the fallback renders inside the app's shell rather than
    ///     replacing it — which is what keeps the <c>&lt;html&gt;</c> attributes and the body class across
    ///     a fault. But the head is built from the components that are actually mounted, and an App whose
    ///     <c>Render()</c> threw contributed none of its own: without this the error page would arrive
    ///     with no charset (mangling any non-ASCII in the message) and no title. Gated on
    ///     <see cref="OwnsDocument" /> because a <em>nested</em> boundary's fallback replaces one widget
    ///     while the rest of the page is fine — retitling the tab "Application error" because a sidebar
    ///     failed would be a worse lie than the missing title it fixes. The registry resolves
    ///     <c>&lt;title&gt;</c> as a singleton with the last contributor winning, so the root fallback
    ///     does replace the app's title, exactly while the fault is on screen.
    /// </remarks>
    protected override Component? Head => OwnsDocument
        ?
        [
            Meta.Charset("utf-8"),
            Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
            Title["Application error"]
        ]
        : null;

    protected override Component? Render()
    {
        var children = new List<Component>
        {
            H1.Style("margin:0 0 0.75rem;font-size:1.5rem;color:#b42323;")["Something went wrong"]
        };

        // Try again first, when the boundary handed us a way to: it keeps the session, the state and the
        // scroll position, where the reload below throws all three away. Offered as the cheaper action
        // rather than the only one — a render that faults deterministically will come straight back here,
        // and then a reload is what you want.
        if (_recover is { } recover)
        {
            children.Add(Button
                .Type("button")
                .Style(ReloadButtonStyle)
                .OnClick(recover)["Try again"]);
        }

        // In-app recovery so the user isn't stranded on the fault: the runtime wires data-rask-reload
        // to location.reload() (CSP-clean, both hosts). If the runtime never loaded, the browser's own
        // reload is the fallback.
        children.Add(Button
            .Type("button")
            .Style(ReloadButtonStyle)
            .Data(new Dictionary<string, string?> { ["rask-reload"] = "" })["Reload this page"]);

        var chain = Unwind(_error);

        // Outermost exception: type + message always (production shows this much); the stack + source
        // excerpts are development-only. In production the chain stops here — inner exceptions, frames,
        // and file paths never reach the response.
        children.AddRange(RenderException(chain[0]));

        if (_isDevelopment)
        {
            for (var i = 1; i < chain.Count; i++)
            {
                children.Add(Details.Open(true)[
                    Summary.Style(CausedByStyle)[
                        $"Caused by: {TypeName(chain[i])}"],
                    Div.Style("padding-left:0.75rem;border-left:2px solid #f5c2c0;margin-top:0.5rem;")[
                        RenderException(chain[i])]
                ]);
            }
        }

        return Div
            .Class("rask-error-boundary")
            .Style("max-width:720px;margin:4rem auto;padding:1.5rem;font-family:system-ui,sans-serif;color:#1f2937;"
                + "border:1px solid #f5c2c0;background:#fff5f5;border-radius:0.5rem;")
            [children];
    }

    // The type + message, plus (development only) a parsed stack with source excerpts. All text is
    // passed as string children, so Rask's Text encodes it — source lines and exception messages can
    // never inject markup.
    private IEnumerable<Component> RenderException(Exception ex)
    {
        yield return P.Style(TypeStyle)[TypeName(ex)];
        yield return Pre.Style(MessageStyle)[ex.Message];

        if (!_isDevelopment)
        {
            yield break;
        }

        foreach (var child in RenderStack(ex))
        {
            yield return child;
        }
    }

    // Development-only: parse the exception's captured frames and, where a frame carries file+line
    // (Debug/PDB builds), show a source excerpt with the throwing line marked. Falls back to the raw
    // StackTrace string when no structured frames are available.
    private static IEnumerable<Component> RenderStack(Exception ex)
    {
        var frames = new StackTrace(ex, fNeedFileInfo: true).GetFrames();
        if (frames is null || frames.Length == 0)
        {
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                yield return Pre.Style(FrameStyle)[ex.StackTrace];
            }

            yield break;
        }

        foreach (var frame in frames)
        {
            var method = DescribeMethod(frame);
            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();

            yield return Div.Style(FrameStyle)[
                file is not null && line > 0 ? $"at {method}  in {file}:line {line}" : $"at {method}"];

            if (file is not null && line > 0 && ReadSourceExcerpt(file, line, SourceRadius) is { } excerpt)
            {
                yield return Pre.Style(ExcerptStyle)[excerpt];
            }
        }
    }

    // Flatten the exception chain, outermost first. AggregateException contributes all of its inner
    // exceptions; every other exception contributes its single InnerException cause. Depth-bounded so a
    // cyclic chain can't loop forever.
    internal static List<Exception> Unwind(Exception root)
    {
        var list = new List<Exception>();

        void Walk(Exception? ex, int depth)
        {
            if (ex is null || depth > MaxChainDepth)
            {
                return;
            }

            list.Add(ex);
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    Walk(inner, depth + 1);
                }
            }
            else
            {
                Walk(ex.InnerException, depth + 1);
            }
        }

        Walk(root, 0);
        return list;
    }

    // A ±radius window of source around the throwing line, with the throwing line marked "→". Returns
    // null (never throws) when the file is missing, unreadable, or the line is out of range — so a
    // deterministic build that stripped source paths, or a deployment without sources, degrades to just
    // the frame line. Only ever called in development (see RenderStack), so no path/source leaks in prod.
    internal static string? ReadSourceExcerpt(string? file, int line, int radius)
    {
        if (string.IsNullOrEmpty(file) || line <= 0)
        {
            return null;
        }

        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var lines = File.ReadAllLines(file);
            if (lines.Length == 0 || line > lines.Length)
            {
                return null;
            }

            var start = Math.Max(1, line - radius);
            var end = Math.Min(lines.Length, line + radius);
            var width = end.ToString().Length;

            var sb = new StringBuilder();
            for (var i = start; i <= end; i++)
            {
                sb.Append(i == line ? "→ " : "  ")
                    .Append(i.ToString().PadLeft(width))
                    .Append(" | ")
                    .Append(lines[i - 1]);
                if (i < end)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }
        catch
        {
            // Never let error-page rendering itself throw (IO error, access denied, path too long, …).
            return null;
        }
    }

    private static string TypeName(Exception ex) => ex.GetType().FullName ?? ex.GetType().Name;

    // DiagnosticMethodInfo is the trim-safe way to describe a frame's method (unlike StackFrame.GetMethod,
    // which carries RequiresUnreferencedCode). Returns just the names, which is all a stack line needs.
    private static string DescribeMethod(StackFrame frame)
    {
        var info = DiagnosticMethodInfo.Create(frame);
        if (info is null)
        {
            return "<unknown>";
        }

        return info.DeclaringTypeName is { } type ? $"{type}.{info.Name}" : info.Name;
    }

    private static bool IsDevelopmentEnvironment() =>
        ResolveIsDevelopment(
            LiveOptions.IsDevelopment,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

    /// <summary>
    ///     The decision itself, as a pure function of its three inputs.
    /// </summary>
    /// <remarks>
    ///     Split out so it can be tested without mutating <see cref="LiveOptions.IsDevelopment" />, which
    ///     is process-global and read by every render that reaches <c>RootErrorBoundary</c> — a test that
    ///     flipped it would be able to change what a concurrently-running test's error page contains.
    ///     <para>
    ///         <paramref name="hostAnswer" /> wins outright. It comes from <c>UseRask</c> via
    ///         <c>IWebHostEnvironment</c>, and is the only input that sees Development selected by
    ///         configuration rather than by a process environment variable — <c>dotnet run
    ///         --environment</c>, <c>appsettings.json</c>, an assigned <c>EnvironmentName</c>, an IDE
    ///         profile. All of those used to yield the production error page while developing (#605).
    ///         The variables remain as the fallback for a standalone host, or a component rendered
    ///         outside one; a host that reports Production is not overridden by a stale shell variable.
    ///     </para>
    /// </remarks>
    internal static bool ResolveIsDevelopment(bool? hostAnswer, string? aspnetEnv, string? dotnetEnv)
    {
        if (hostAnswer is { } resolved)
        {
            return resolved;
        }

        var env = aspnetEnv ?? dotnetEnv;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
