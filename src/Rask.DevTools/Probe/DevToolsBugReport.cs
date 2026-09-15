using System.Text;

namespace Rask.DevTools.Probe;

/// <summary>What a stack says about who is to blame: the framework, or the app.</summary>
/// <param name="LikelyFrameworkBug">The innermost frame that is neither the runtime's nor the base library's is Rask's.</param>
/// <param name="Frames">
///     The frames a report may carry, innermost first: Rask's, by name only, and every run of anything else collapsed to
///     <c>[app code]</c>. No file paths, no arguments.
/// </param>
internal sealed record DevToolsStackVerdict(bool LikelyFrameworkBug, IReadOnlyList<string> Frames)
{
    internal static readonly DevToolsStackVerdict None = new(false, []);
}

/// <summary>
///     The "Report framework bug" button's report: which errors get one, and what it says.
/// </summary>
/// <remarks>
///     <para>
///         Only an error that looks like the framework's own gets the button, decided from its stack: the innermost frame
///         outside the .NET runtime and base library is in a Rask namespace — or, for a script on the page, in one of
///         Rask's scripts. An exception thrown in the app's own code never qualifies, even when Rask called that code.
///     </para>
///     <para>
///         What a report says is minimal on purpose, because it leaves the machine: versions, the host, the exception's
///         type, Rask's own frames by name with the app's collapsed, and the components' type names. Never the message,
///         never a prop or a payload, never a file path. The developer sees and edits all of it before GitHub does.
///     </para>
///     <para>
///         Frames are read from the stack's text rather than through reflection, so a trimmed WASM app reports the same as
///         one on the JIT, and nothing here needs metadata the trimmer may have removed.
///     </para>
/// </remarks>
internal static class DevToolsBugReport
{
    /// <summary>Where a report is filed.</summary>
    internal const string NewIssueUrl = "https://github.com/pal-tamas/rask/issues/new";

    /// <summary>How long an issue URL may grow; older frames are dropped until it fits.</summary>
    internal const int UrlLimit = 8000;

    private const string AppCode = "[app code]";
    private const int FrameLimit = 30;

    // The script files that are Rask's own on a page: the runtimes, the island runtime, anything a Rask package serves.
    private static readonly string[] RaskScripts = ["/rask/rask.js", "rask.wasm", "rask-external.js", "/_content/Rask.", "/_rask/"];

    /// <summary>
    ///     Reads a .NET stack (<see cref="Exception.StackTrace" />). <paramref name="appNamespaces" /> are namespaces known to
    ///     be the app's — its entry assembly's name, the failing component's namespace — so an app that happens to live
    ///     under <c>Rask.</c> is still an app.
    /// </summary>
    internal static DevToolsStackVerdict FromDotNet(string? stack, IReadOnlyCollection<string> appNamespaces)
    {
        if (string.IsNullOrEmpty(stack))
        {
            return DevToolsStackVerdict.None;
        }

        var frames = new List<string>();
        bool? framework = null;
        foreach (var raw in stack.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal))
            {
                continue;
            }

            var member = line[3..];
            var open = member.IndexOf('(', StringComparison.Ordinal);
            if (open > 0)
            {
                member = member[..open];
            }

            if (IsRuntime(member))
            {
                continue;
            }

            var isRask = IsRask(member, appNamespaces);
            framework ??= isRask;
            Add(frames, isRask ? member : AppCode);
        }

        return new DevToolsStackVerdict(framework == true, frames);
    }

    /// <summary>Reads a script's stack as a browser writes it (<c>at fn (url:line:col)</c> or <c>fn@url:line:col</c>).</summary>
    internal static DevToolsStackVerdict FromScript(string? stack)
    {
        if (string.IsNullOrEmpty(stack))
        {
            return DevToolsStackVerdict.None;
        }

        var frames = new List<string>();
        bool? framework = null;
        foreach (var raw in stack.Split('\n'))
        {
            var line = raw.Trim();
            var url = ScriptUrl(line);
            if (url is null)
            {
                continue;
            }

            var isRask = RaskScripts.Any(s => url.Contains(s, StringComparison.Ordinal));
            framework ??= isRask;
            Add(frames, isRask ? ScriptFrame(line, url) : AppCode);
        }

        return new DevToolsStackVerdict(framework == true, frames);
    }

    /// <summary>The runtime facts a report carries, gathered where the panel runs.</summary>
    internal sealed record Environment(string Host, string RaskVersion, string DotNet, string Os, string? Browser);

    /// <summary>The issue's title and body, before the developer edits them.</summary>
    internal static (string Title, string Body) Draft(DevToolsError error, Environment environment)
    {
        var where = error.Kind switch
        {
            DevToolsErrorKind.Render => "rendering",
            DevToolsErrorKind.Handler => "an event handler",
            DevToolsErrorKind.Lifecycle => "a lifecycle hook",
            DevToolsErrorKind.Page => "a page script",
            DevToolsErrorKind.Island => "an island",
            _ => "the framework",
        };
        var innermost = error.ReportFrames.FirstOrDefault(f => f != AppCode);
        var title = innermost is null
            ? $"{error.Title} in {where}"
            : $"{error.Title} in {innermost}";

        var body = new StringBuilder();
        body.Append("**What happened:** `").Append(error.Title).Append("` in ").Append(where).Append('\n');
        if (error.Path.Count > 0)
        {
            body.Append("**Components:** `").Append(string.Join(" › ", error.Path)).Append("`\n");
        }

        body.Append("**Host:** ").Append(environment.Host)
            .Append(" · Rask ").Append(environment.RaskVersion)
            .Append(" · ").Append(environment.DotNet)
            .Append(" · ").Append(environment.Os);
        if (environment.Browser is { Length: > 0 } browser)
        {
            body.Append(" · ").Append(browser);
        }

        body.Append("\n\n**Stack** (Rask's frames; the app's collapsed):\n```\n");
        foreach (var frame in error.ReportFrames)
        {
            body.Append(frame == AppCode ? AppCode : "at " + frame).Append('\n');
        }

        body.Append("```\n\n**What were you doing when it happened?**\n\n")
            .Append("<!-- Reported from Rask DevTools. Nothing else from the app is included: no exception message, props, ")
            .Append("data or file paths. Add what you can share. -->\n");
        return (title, body.ToString());
    }

    /// <summary>
    ///     The GitHub URL that opens a new issue filled with <paramref name="title" /> and <paramref name="body" />, labelled
    ///     as a bug — shortened, when it would pass <see cref="UrlLimit" />, by dropping stack lines from the end, then by
    ///     cutting the body.
    /// </summary>
    internal static string IssueUrl(string title, string body)
    {
        string Url(string b) =>
            NewIssueUrl + "?labels=bug&title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(b);

        var url = Url(body);
        if (url.Length <= UrlLimit)
        {
            return url;
        }

        // The oldest frames go first: the innermost ones say where it broke.
        var lines = body.Split('\n').ToList();
        var fence = lines.FindLastIndex(l => l == "```");
        while (url.Length > UrlLimit && fence > 1 && lines[fence - 1] != "```" && !lines[fence - 1].StartsWith("**", StringComparison.Ordinal))
        {
            lines.RemoveAt(fence - 1);
            fence--;
            url = Url(string.Join('\n', lines));
        }

        var shortened = string.Join('\n', lines);
        while (url.Length > UrlLimit && shortened.Length > 0)
        {
            shortened = shortened[..(shortened.Length * 3 / 4)];
            url = Url(shortened);
        }

        return url;
    }

    private static void Add(List<string> frames, string frame)
    {
        if (frames.Count >= FrameLimit || (frame == AppCode && frames.Count > 0 && frames[^1] == AppCode))
        {
            return;
        }

        frames.Add(frame);
    }

    private static bool IsRuntime(string member) =>
        member.StartsWith("System.", StringComparison.Ordinal)
        || member.StartsWith("Microsoft.", StringComparison.Ordinal)
        || member.StartsWith("Interop.", StringComparison.Ordinal);

    private static bool IsRask(string member, IReadOnlyCollection<string> appNamespaces) =>
        (member.StartsWith("Rask.", StringComparison.Ordinal))
        && !appNamespaces.Any(ns => ns.Length > 0 && (member.StartsWith(ns + ".", StringComparison.Ordinal)));

    // The URL of a browser stack line, or null for a line that is not a frame.
    private static string? ScriptUrl(string line)
    {
        var open = line.LastIndexOf('(');
        var close = line.LastIndexOf(')');
        var location = open >= 0 && close > open ? line[(open + 1)..close]
            : line.StartsWith("at ", StringComparison.Ordinal) ? line[3..]
            : line.Contains('@', StringComparison.Ordinal) ? line[(line.IndexOf('@', StringComparison.Ordinal) + 1)..]
            : null;
        return location is not null && (location.Contains("://", StringComparison.Ordinal) || location.StartsWith('/'))
            ? location
            : null;
    }

    // "fn (rask.js:1:2345)": the function and the file's name, never the host or the directory it was served from.
    private static string ScriptFrame(string line, string url)
    {
        var file = url.Split('?')[0];
        file = file[(file.LastIndexOf('/') + 1)..];
        var function = line.StartsWith("at ", StringComparison.Ordinal)
            ? line[3..].Split(" (")[0]
            : line.Contains('@', StringComparison.Ordinal) ? line[..line.IndexOf('@', StringComparison.Ordinal)] : "";
        return function.Length == 0 || function.Contains("://", StringComparison.Ordinal) ? file : $"{function} ({file})";
    }
}
