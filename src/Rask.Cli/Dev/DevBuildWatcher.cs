using System.Text.RegularExpressions;

namespace Rask.Cli.Dev;

/// <summary>What <c>rask dev</c> currently believes about the app it is running.</summary>
internal enum DevBuildState
{
    /// <summary>The app built and is running (or is expected to be).</summary>
    Ok,

    /// <summary>A rebuild is in flight. The app may be momentarily down; that is not a failure.</summary>
    Building,

    /// <summary>The build failed. The app is down and will not come back until the code compiles.</summary>
    Failed,
}

/// <summary>
///     Reads <c>dotnet watch</c>'s output line by line and tracks whether the app is buildable.
/// </summary>
/// <remarks>
///     <para>
///         A pure state machine over lines, deliberately: it is the half of #603 that has to be exactly
///         right, and a pure function of its input is the half that can be tested without a compiler, a
///         file watcher or a browser.
///     </para>
///     <para>
///         <b>It keys on the compiler's own diagnostic format, not on <c>dotnet watch</c>'s prose.</b>
///         Watch decorates its lines with emoji that <c>DOTNET_WATCH_SUPPRESS_EMOJIS</c> strips, localises
///         its own messages, and has changed their wording between SDK releases; MSBuild's
///         <c>path(line,col): error CS0103: text</c> is stable, documented and locale-independent in its
///         <c>error</c>/<c>warning</c> keyword. Matching the diagnostics rather than the narration is what
///         keeps this from breaking on the next SDK.
///     </para>
/// </remarks>
internal sealed class DevBuildWatcher
{
    // `severity CODE: text`, wherever it appears in the line. Both forms in the wild reach it:
    //
    //   /app/Pages/Home.cs(12,9): error CS0103: The name 'x' does not exist…      (MSBuild)
    //   dotnet watch ❌ error CS7038: Failed to emit module 'App'…                 (watch itself)
    //
    // Anchoring the prefix — as this first did — silently misses the second, which is the *only* form a
    // failed hot-reload emit is reported in, so the panel never appeared for the commonest failure there
    // is. The prefix is captured but only kept when it ends in a colon, which is exactly what separates a
    // real file location from watch's own decoration.
    private static readonly Regex Diagnostic = new(
        @"^(?<origin>.*?)(?<![A-Za-z0-9])(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<text>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Colour, kept deliberately on the way to the terminal (rask dev sets
    // DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION so redirecting watch's output doesn't drain it of
    // colour) — and equally deliberately stripped here, because the panel renders text, not a terminal.
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    // Watch's own byline — `dotnet watch ❌ `, `dotnet watch ⌚ ` — which it puts in front of everything,
    // including a diagnostic that already has a file location:
    //
    //   dotnet watch ❌ /app/Pages/Home.cs(12,9): error CS0103: The name 'x' does not exist…
    //
    // The location-ends-in-a-colon rule below cannot drop it there, because the byline is *part* of the
    // prefix that ends in a colon. Stripped up front instead. The character class stops at the first
    // letter, digit or path character, so it eats the emoji and the spaces and nothing else — and it
    // leaves the line alone when DOTNET_WATCH_SUPPRESS_EMOJIS removed the symbol already.
    private static readonly Regex WatchByline = new(@"^dotnet watch[^\p{L}\p{N}/\\._]*", RegexOptions.Compiled);

    // Watch announces a rebuild before it starts one. Matched loosely (contains, case-insensitive) because
    // the surrounding decoration varies; a miss only costs a slightly late transition to Building, which
    // no one sees, where a false positive would clear a real failure. "file updated" is what .NET 10's
    // watch actually prints — "file changed" is kept for the SDKs that said that.
    private static readonly string[] RebuildMarkers =
    [
        "file changed", "file updated", "restarting", "building",
    ];

    // …and what it prints when one has finished cleanly. Taken from what .NET 10's watch actually emits —
    // MSBuild's "Build succeeded.", watch's "C# and Razor changes applied in 456ms." / "Hot reload of
    // changes succeeded." and its "No managed code changes to apply." (what a save reverting a broken edit
    // produces). All observed, not guessed — the applied-form is the one a working edit emits. Without these
    // the machine would sit on Building forever after a recovery: harmless for the client, which only
    // acts on "failed", but a state nobody could explain.
    private static readonly string[] SuccessMarkers =
    [
        "build succeeded", "changes succeeded", "changes applied", "no managed code changes", "started",
    ];

    // What watch prints when it has given up and is idling — "Waiting for a file to change before
    // restarting dotnet". It contains a rebuild marker but means the exact opposite of one, and it is the
    // line that immediately follows a failed build: matching it would erase the failure a moment after
    // detecting it, which is the whole feature.
    private const string IdleMarker = "waiting";

    // Errors are collected per build, so a rebuild that fixes one of three does not leave the other two
    // on screen. Cleared when a rebuild starts, not when it succeeds — a build that emits nothing at all
    // is a success, and there is no line to key that on.
    private readonly List<string> _errors = [];
    private readonly Lock _gate = new();
    private bool _sawErrorThisBuild;

    /// <summary>The current state. Safe to read from the status server's thread.</summary>
    public DevBuildState State
    {
        get
        {
            lock (_gate)
            {
                return _sawErrorThisBuild ? DevBuildState.Failed : _state;
            }
        }
    }

    private DevBuildState _state = DevBuildState.Ok;

    /// <summary>The compiler errors from the current build, in the order they were reported.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors.ToArray();
            }
        }
    }

    /// <summary>Feeds one line of watch's output through the machine.</summary>
    public void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var clean = WatchByline.Replace(AnsiEscape.Replace(line, string.Empty).Trim(), string.Empty).Trim();
        if (clean.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var match = Diagnostic.Match(clean);
            if (match.Success)
            {
                if (!match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase))
                {
                    // A warning is not a verdict — but it must not fall through to the marker scan either.
                    // Warnings carry a file path, and a path containing "building" or "started" would
                    // otherwise clear a real failure the compiler had just reported.
                    return;
                }

                // Dedup: MSBuild repeats a diagnostic once per project that references the faulting one,
                // so a single typo in a shared library arrives three times in a wasm-hosted solution. It
                // dedups on the *rendered* text, so the same error reported through two different
                // decorations still counts once.
                var text = Render(match);
                if (!_errors.Contains(text, StringComparer.Ordinal))
                {
                    _errors.Add(text);
                }

                _sawErrorThisBuild = true;
                _state = DevBuildState.Failed;
                return;
            }

            if (clean.Contains(IdleMarker, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Success first: a line can carry both ("Build succeeded" after "Building"), and the finished
            // verdict is the more specific one.
            foreach (var marker in SuccessMarkers)
            {
                if (clean.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    _errors.Clear();
                    _sawErrorThisBuild = false;
                    _state = DevBuildState.Ok;
                    return;
                }
            }

            foreach (var marker in RebuildMarkers)
            {
                if (clean.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    // A new build supersedes the last one's verdict: the errors on screen belong to a
                    // build that no longer describes the code on disk.
                    _errors.Clear();
                    _sawErrorThisBuild = false;
                    _state = DevBuildState.Building;
                    return;
                }
            }
        }
    }

    /// <summary>
    ///     The one line the panel shows for a diagnostic — location (when there is one), code, message.
    /// </summary>
    /// <remarks>
    ///     A real location ends in a colon (<c>Home.cs(12,9):</c>); watch's own decoration
    ///     (<c>dotnet watch ❌</c>) does not, and is dropped. Rebuilding the line rather than keeping it
    ///     verbatim is also what lets the same error reported through two decorations dedup as one.
    /// </remarks>
    private static string Render(Match match)
    {
        var origin = match.Groups["origin"].Value.Trim();
        var body = "error " + match.Groups["code"].Value + ": " + match.Groups["text"].Value.Trim();
        return origin.EndsWith(':') ? origin + " " + body : body;
    }

    /// <summary>The status document the client polls, as JSON.</summary>
    public string ToJson()
    {
        DevBuildState state;
        string[] errors;
        lock (_gate)
        {
            state = _sawErrorThisBuild ? DevBuildState.Failed : _state;
            errors = _errors.ToArray();
        }

        var name = state switch
        {
            DevBuildState.Failed => "failed",
            DevBuildState.Building => "building",
            _ => "ok",
        };

        // Hand-written rather than serialized: three fields, no reflection, and the shape is a contract
        // the client branches on — spelling it out here is what makes it reviewable next to the client.
        var detail = string.Join("\n", errors);
        var first = errors.Length > 0 ? errors[0] : string.Empty;
        return "{\"state\":\"" + name + "\","
               + "\"count\":" + errors.Length + ","
               + "\"message\":" + JsonString(first) + ","
               + "\"detail\":" + JsonString(detail) + "}";
    }

    internal static string JsonString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
