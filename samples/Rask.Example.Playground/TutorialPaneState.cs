namespace Rask.Example.Playground;

/// <summary>Which list the left pane is showing — the guided track, or the free-form example gallery.</summary>
internal enum PlaygroundTab
{
    Tutorial,
    Examples
}

/// <summary>How a chapter appears in the tutorial list.</summary>
/// <remarks>
///     Deliberately <b>not</b> a "done" state: a chapter can be the one you are reading <i>and</i> one you
///     have already compiled, and the two have to be readable independently — the E2E asserts a chapter is
///     ticked off while it is still the active one, which a single winner-takes-all state cannot express.
///     <see cref="TutorialPaneState.ClassesFor" /> composes the tick on top of the state.
/// </remarks>
internal enum ChapterState
{
    /// <summary>Not the loaded chapter, and available to open.</summary>
    Open,

    /// <summary>The chapter currently loaded in the editor.</summary>
    Active,

    /// <summary>Needs the database, on a build that ships without it.</summary>
    Locked
}

/// <summary>
///     The classes and ids that carry the tutorial pane's state into the DOM, so the browser E2E can drive
///     the track by state rather than by the label a chapter happens to have.
/// </summary>
/// <remarks>
///     Same test-contract reasoning as <see cref="IdeBadgeState" />, and for the same reason: a locator that
///     resolves to nothing fails by <i>timing out</i>, so a redesign that quietly drops a hook reports as a
///     slow, misattributed browser failure instead of naming itself. Keeping the mapping in a file the unit
///     test project links means the fast gate catches it and says what broke.
/// </remarks>
internal static class TutorialPaneState
{
    /// <summary>Positioning hook on every chapter button — the other half of each E2E selector.</summary>
    public const string ChapterClass = "pg-chapter";

    /// <summary>Positioning hook on the two pane tabs.</summary>
    public const string TabClass = "pg-tab";

    public const string Open = "is-open";
    public const string Active = "is-active";
    public const string Done = "is-done";
    public const string Locked = "is-locked";

    /// <summary>Every state class a chapter can render — what a selector has to be one of to resolve.</summary>
    public static readonly string[] All = [Open, Active, Done, Locked];

    public static string ClassFor(ChapterState state) => state switch
    {
        ChapterState.Active => Active,
        ChapterState.Locked => Locked,
        _ => Open
    };

    /// <summary>
    ///     The full class list for one chapter button: its state, plus the completion tick if it has been
    ///     compiled. The two are independent — the chapter you just ran is both active and done.
    /// </summary>
    public static string ClassesFor(ChapterState state, bool done) =>
        done ? $"{ChapterClass} {ClassFor(state)} {Done}" : $"{ChapterClass} {ClassFor(state)}";

    /// <summary>A stable per-chapter hook, so the E2E can open chapter 5 without depending on its title.</summary>
    public static string ChapterId(int number) => $"pg-chapter-{number}";

    /// <summary>A stable per-tab hook, for the same reason.</summary>
    public static string TabId(PlaygroundTab tab) =>
        tab == PlaygroundTab.Tutorial ? "pg-tab-tutorial" : "pg-tab-examples";
}
