using System.Diagnostics;
using System.Text;

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

    public DefaultErrorPage(Exception error) : this(error, IsDevelopmentEnvironment())
    {
    }

    // Test seam: construct with an explicit environment so unit tests need no process-global env var
    // (setting ASPNETCORE_ENVIRONMENT would race other tests that render this page).
    internal DefaultErrorPage(Exception error, bool isDevelopment)
    {
        _error = error;
        _isDevelopment = isDevelopment;
    }

    protected override bool BypassRenderCache => true;

    protected override Component? Render()
    {
        var children = new List<Component>
        {
            Generated.H1(Style: "margin:0 0 0.75rem;font-size:1.5rem;color:#b42323;")["Something went wrong"],
            // In-app recovery so the user isn't stranded on the fault: the runtime wires data-rask-reload
            // to location.reload() (CSP-clean, both hosts). If the runtime never loaded, the browser's own
            // reload is the fallback.
            Generated.Button(
                Type: "button",
                Style: ReloadButtonStyle,
                Data: new Dictionary<string, string?> { ["rask-reload"] = "" })["Reload this page"]
        };

        var chain = Unwind(_error);

        // Outermost exception: type + message always (production shows this much); the stack + source
        // excerpts are development-only. In production the chain stops here — inner exceptions, frames,
        // and file paths never reach the response.
        children.AddRange(RenderException(chain[0]));

        if (_isDevelopment)
        {
            for (var i = 1; i < chain.Count; i++)
            {
                children.Add(Generated.Details(Open: true)[
                    Generated.Summary(Style: CausedByStyle)[
                        $"Caused by: {TypeName(chain[i])}"],
                    Generated.Div(Style: "padding-left:0.75rem;border-left:2px solid #f5c2c0;margin-top:0.5rem;")[
                        RenderException(chain[i])]
                ]);
            }
        }

        return Generated.Div(
                Class: "rask-error-boundary",
                Style:
                "max-width:720px;margin:4rem auto;padding:1.5rem;font-family:system-ui,sans-serif;color:#1f2937;"
                + "border:1px solid #f5c2c0;background:#fff5f5;border-radius:0.5rem;")
            [children];
    }

    // The type + message, plus (development only) a parsed stack with source excerpts. All text is
    // passed as string children, so Rask's Text encodes it — source lines and exception messages can
    // never inject markup.
    private IEnumerable<Component> RenderException(Exception ex)
    {
        yield return Generated.P(Style: TypeStyle)[TypeName(ex)];
        yield return Generated.Pre(Style: MessageStyle)[ex.Message];

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
                yield return Generated.Pre(Style: FrameStyle)[ex.StackTrace];
            }

            yield break;
        }

        foreach (var frame in frames)
        {
            var method = DescribeMethod(frame);
            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();

            yield return Generated.Div(Style: FrameStyle)[
                file is not null && line > 0 ? $"at {method}  in {file}:line {line}" : $"at {method}"];

            if (file is not null && line > 0 && ReadSourceExcerpt(file, line, SourceRadius) is { } excerpt)
            {
                yield return Generated.Pre(Style: ExcerptStyle)[excerpt];
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

    private static bool IsDevelopmentEnvironment()
    {
        // Read the standard ASP.NET environment variables so we can gate stack traces without
        // taking a Microsoft.Extensions.Hosting dependency on the framework core.
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
