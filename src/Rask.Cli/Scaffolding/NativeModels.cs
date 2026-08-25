namespace Rask.Cli.Scaffolding;

/// <summary>
///     One native hosting model (<c>--host</c>), and the three things that actually differ between them:
///     <b>where your code runs</b>, <b>what works offline</b>, and <b>what hot-reloads</b>. Everything else
///     about a Rask native app — the same <c>Screen</c>, the same device APIs, the same
///     <c>rask new</c> — is the same in every model, which is the whole point of the axis. Those three are
///     the honest exceptions, so they are printed rather than left to be discovered.
/// </summary>
/// <param name="Id">The <c>--host</c> value.</param>
/// <param name="Title">The model in two or three words, for a chooser.</param>
/// <param name="Where">Where the component code runs.</param>
/// <param name="Offline">What the app can do with no network.</param>
/// <param name="Reload">What an edit costs before you see it.</param>
/// <param name="Summary">
///     How the model is described back to whoever just scaffolded it — reads after "UI from ".
/// </param>
internal sealed record NativeModel(
    string Id,
    string Title,
    string Where,
    string Offline,
    string Reload,
    string Summary)
{
    /// <summary>
    ///     Whether the components run in the app itself. The other models are a native shell over a Rask
    ///     app you host, and scaffold both halves as one solution.
    /// </summary>
    public bool RunsInProcess => string.Equals(Id, NativeModels.InProcess, StringComparison.Ordinal);

    /// <summary>The trade-off in one line — what a chooser shows beside the title.</summary>
    public string TradeOff => $"{Where} · offline: {Offline} · reload: {Reload}";
}

/// <summary>
///     The <c>--host</c> axis. One list, so the choice values, the help text, the wizard, the post-scaffold
///     summary and the generator cannot drift apart — and so a new model is added in one place.
/// </summary>
internal static class NativeModels
{
    /// <summary>The <c>--host</c> value whose components run on the device.</summary>
    public const string InProcess = "native";

    /// <summary>
    ///     Every model, in the order a chooser should offer them: the one that needs nothing else first,
    ///     then the two that point at an app you host.
    /// </summary>
    public static readonly IReadOnlyList<NativeModel> All =
    [
        new(InProcess,
            Title: "The device",
            Where: "runs in the app",
            Offline: "everything",
            Reload: "restart the app",
            Summary: "the device (works offline)"),
        new("server",
            Title: "A Rask server",
            Where: "runs on your server",
            Offline: "nothing — it needs the connection",
            Reload: "live",
            Summary: "a Rask server you host"),
        new("wasm-hosted",
            Title: "A wasm-hosted app",
            Where: "runs in the WebView",
            Offline: "after it has loaded once",
            Reload: "republish the client",
            Summary: "a wasm-hosted app you host"),
    ];

    /// <summary>The valid <c>--host</c> values, in chooser order — the schema's choice list.</summary>
    public static string[] Ids => [.. All.Select(m => m.Id)];

    /// <summary>The valid values as they read in an error or a help string: <c>native|server|…</c>.</summary>
    public static string IdList => string.Join('|', All.Select(m => m.Id));

    /// <summary>
    ///     The model for a <c>--host</c> value. Unknown values cannot reach here — the schema rejects them
    ///     against <see cref="Ids" /> first — so an unrecognised one falls back to the in-process model
    ///     rather than throwing, which is what every call site wants.
    /// </summary>
    public static NativeModel For(string? host) =>
        All.FirstOrDefault(m => string.Equals(m.Id, host, StringComparison.Ordinal)) ?? All[0];
}
