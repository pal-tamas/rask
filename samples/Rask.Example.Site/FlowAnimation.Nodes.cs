namespace Rask.Example.Site;

// The map itself: every band, every box, and the packages each box stands for.
//
// One declarative table rather than hand-placed shapes, for a reason this repo has learned the hard way
// more than once -- a diagram drawn by hand goes stale silently, and the staleness is only ever found by
// somebody reading it. Here the table is the single source for the geometry, the label, the wires and
// the coverage assertion, so `tests/Rask.Example.Site.Tests` can walk `src/` and fail the build when a
// package exists with no box to stand in. Adding a package is then a compile-and-test failure, not a
// slow drift.
internal sealed partial class FlowAnimation
{
    /// <summary>One box on the map.</summary>
    /// <param name="Key">Stable, used for the CSS class that lights or dims it.</param>
    /// <param name="Packages">
    ///     The <c>src/</c> project directories this box stands for. The union across every node must
    ///     cover the repository; that is asserted, not assumed.
    /// </param>
    /// <param name="Scenes">Which journeys light this box up.</param>
    private sealed record Node(
        string Key,
        double X,
        double Y,
        double W,
        double H,
        string Label,
        string? Sub,
        string Kind,
        string[] Packages,
        int[] Scenes);

    private const int SceneServer = (int)FlowScene.ServerRoundTrip;
    private const int SceneWasm = (int)FlowScene.WasmTakeover;
    private const int SceneIslands = (int)FlowScene.Islands;
    private const int SceneSpa = (int)FlowScene.Spa;
    private const int SceneDurable = (int)FlowScene.Durable;

    private static readonly int[] AllScenes =
        [SceneServer, SceneWasm, SceneIslands, SceneSpa, SceneDurable];

    // ---- the spine ----
    private static readonly Node[] SpineNodes =
    [
        // FRONT END -------------------------------------------------------------------------------
        // The mock browser is a first-class node, not decoration: it is where the journey starts and
        // the only place the reader can SEE the result land.
        new("browser", 196, 84, 300, 126, "localhost:5000/orders", "the page a visitor sees",
            "screen", [], AllScenes),
        // Row labels. Only the island row carries packages, because only the island row IS one — and it
        // carries TWO, because the two kinds of island are implemented by different packages:
        // Rask.External hosts a runtime that owns its own DOM, Rask.Blazor renders .razor itself.
        // Wider than the label needs so the two-package sub-label clears the chips that follow.
        new("lane-rask", 500, 88, 134, 28, "Rask — C#", null, "lane", [], [SceneServer, SceneWasm, SceneDurable]),
        new("lane-island", 500, 124, 134, 28, "Islands", "Rask.External · Rask.Blazor",
            "lane", ["Rask.External", "Rask.Blazor"], [SceneIslands]),
        new("lane-spa", 500, 160, 134, 28, "SPA lane", null, "lane", [], [SceneSpa]),

        new("chain", 640, 88, 396, 28, "Component chain — Rask.Core · Rask.Html", null,
            "chip-wide", ["Rask.Core", "Rask.Html"], [SceneServer, SceneWasm, SceneIslands, SceneDurable]),
        new("validation", 640, 192, 196, 20, "Rask.Validation.*", null,
            "chip-note", ["Rask.Validation.DataAnnotations", "Rask.Validation.FluentValidation"], [SceneServer]),
        new("testing", 844, 192, 192, 20, "Rask.Testing", null,
            "chip-note", ["Rask.Testing"], []),

        // TRANSPORT -------------------------------------------------------------------------------
        new("ws", 196, 246, 270, 36, "WebSocket  /rask/ws", "Rask.Server",
            "wire", ["Rask.Server"], [SceneServer, SceneIslands, SceneDurable]),
        new("jsi", 486, 246, 260, 36, "JSImport / JSExport", "Rask.Wasm · Rask.Wasm.Hosting",
            "wire", ["Rask.Wasm", "Rask.Wasm.Hosting"], [SceneWasm]),
        new("http", 766, 246, 278, 36, "HTTP  /_rask/cqrs/request/{name}",
            "Rask.Cqrs.Client → Rask.Cqrs.Server · Rask.Spa.Hosting",
            "wire", ["Rask.Cqrs.Client", "Rask.Cqrs.Server", "Rask.Spa.Hosting"], [SceneSpa]),

        // RENDER CORE + LADDER --------------------------------------------------------------------
        new("serialize", 196, 336, 100, 34, "HtmlSerializer", null, "core", [], [SceneServer, SceneWasm, SceneIslands]),
        new("frames", 308, 336, 100, 34, "RenderFrame[]", null, "core", [], [SceneServer, SceneWasm, SceneIslands]),
        new("differ", 420, 336, 100, 34, "FrameDiffer", null, "core", [], [SceneServer, SceneWasm, SceneIslands]),
        new("payload", 532, 336, 104, 34, "EditOp[] → payload", null, "core", [], [SceneServer, SceneWasm, SceneIslands]),

        new("rung0", 660, 336, 90, 34, "static doc", null, "rung", [], []),
        new("rung1", 756, 336, 90, 34, "streaming", "not built", "rung-off", [], []),
        new("rung2", 852, 336, 90, 34, "live / WS", null, "rung", [], [SceneServer, SceneIslands, SceneDurable]),
        new("rung3", 948, 336, 90, 34, "WASM", null, "rung", [], [SceneWasm]),

        // APP LOGIC -------------------------------------------------------------------------------
        new("query", 196, 418, 200, 34, "Rask.Query", "IQueryClient — dedup, staleness",
            "app", ["Rask.Query"], [SceneServer, SceneWasm]),
        new("cqrs", 416, 418, 260, 34, "Rask.Cqrs — IDispatcher", "one mediator, reflection-free",
            "app", ["Rask.Cqrs"], AllScenes),
        new("handlers", 696, 418, 200, 34, "your handlers", "ICommandHandler / IQueryHandler",
            "app", [], AllScenes),
        new("meta", 916, 418, 128, 34, "Rask", "the one package",
            "app-soft", ["Rask"], []),

        // DURABLE BATTERIES -----------------------------------------------------------------------
        new("jobs", 196, 502, 150, 46, "Rask.Jobs", "IJob · Job table",
            "batt", ["Rask.Jobs"], [SceneDurable]),
        new("mail", 362, 502, 150, 46, "Rask.Mail", "IMail · QueuedMail",
            "batt", ["Rask.Mail"], [SceneDurable]),
        new("cache", 528, 502, 150, 46, "Rask.Cache", "ICache · CacheEntry",
            "batt", ["Rask.Cache"], []),
        new("outbox", 694, 502, 150, 46, "Rask.Outbox", "same transaction",
            "batt", ["Rask.Outbox"], [SceneServer, SceneDurable]),
        new("logging", 860, 502, 184, 46, "Rask.Logging", "its own SQLite file",
            "batt-aside", ["Rask.Logging"], []),

        // DATA ------------------------------------------------------------------------------------
        new("data", 196, 600, 200, 34, "Rask.Data", "audit · soft delete · events",
            "data", ["Rask.Data"], [SceneServer, SceneSpa, SceneDurable]),
        new("ef", 406, 600, 120, 34, "EF Core", null,
            "data", [], [SceneServer, SceneSpa, SceneDurable]),
        new("sqliteef", 536, 600, 180, 34, "UseRaskSqlite", "Rask.SQLite.EntityFrameworkCore",
            "data", ["Rask.SQLite.EntityFrameworkCore"], [SceneServer, SceneSpa, SceneDurable]),
        new("sqlite", 726, 600, 150, 34, "Rask.SQLite", "WAL · busy_timeout",
            "data", ["Rask.SQLite"], [SceneServer, SceneSpa, SceneDurable]),
        new("browserdb", 886, 600, 158, 34, "Rask.SQLite.Browser", "SQLite in the tab",
            "data-soft", ["Rask.SQLite.Browser"], [SceneWasm]),

        // STORAGE ---------------------------------------------------------------------------------
        new("appdb", 196, 684, 220, 48, "app.db  +  -wal", "orders · outbox · jobs · mail · cache",
            "store", [], [SceneServer, SceneSpa, SceneDurable]),
        new("logsdb", 436, 684, 160, 48, "logs.db", "not backed up",
            "store-soft", [], []),
        new("snapdir", 616, 684, 160, 48, "snapshots/", "Online Backup API",
            "store-soft", [], []),
        new("objstore", 796, 684, 248, 48, "object storage", "S3 · GCS · Azure · file",
            "store-soft", [], []),
    ];

    // ---- the left rail: everything that happens before the app ever runs ----
    private static readonly Node[] BuildNodes =
    [
        new("gen", LeftRailX, 96, RailW, 40, "Rask.Generators", "factories · Routes · RASK0xx",
            "rail", ["Rask.Generators"], AllScenes),
        new("genfix", LeftRailX, 144, RailW, 40, "…CodeFixes", "IDE quick fixes",
            "rail", ["Rask.Generators.CodeFixes"], []),
        new("battgen", LeftRailX, 192, RailW, 40, "Rask.Batteries.Generators", "CQRS codec · registries",
            "rail", ["Rask.Batteries.Generators"], [SceneSpa]),
        new("tstasks", LeftRailX, 240, RailW, 40, "Rask.TypeScript.Tasks", "scoped TS",
            "rail", ["Rask.TypeScript.Tasks"], []),
        new("wasmtasks", LeftRailX, 288, RailW, 40, "Rask.Wasm.Tasks", "scoped-asset bake",
            "rail", ["Rask.Wasm.Tasks"], [SceneWasm]),
        new("spatasks", LeftRailX, 336, RailW, 40, "Rask.Spa.Tasks", "TypeScript from C#",
            "rail", ["Rask.Spa.Tasks"], [SceneSpa]),
        new("exttasks", LeftRailX, 384, RailW, 40, "Rask.External.Tasks", "island bundles · prop types",
            "rail", ["Rask.External.Tasks"], [SceneIslands]),
        new("tw", LeftRailX, 432, RailW, 40, "Rask.Tailwind", "no npm, no config",
            "rail", ["Rask.Tailwind"], []),
        new("twtasks", LeftRailX, 480, RailW, 40, "Rask.Tailwind.Tasks", "resolves the CLI",
            "rail", ["Rask.Tailwind.Tasks"], []),
        new("cli", LeftRailX, 528, RailW, 40, "Rask.Cli", "new · db · dev · deploy",
            "rail", ["Rask.Cli"], []),
    ];

    // ---- the right rail: what an operator looks at, and where the bytes are copied ----
    private static readonly Node[] OpsNodes =
    [
        new("dashboard", RightRailX, 420, RailW, 56, "Rask.Dashboard", "/_rask — queues & logs",
            "ops", ["Rask.Dashboard"], [SceneDurable]),
        new("litestream", RightRailX, 492, RailW, 52, "…Litestream", "continuous WAL",
            "ops", ["Rask.SQLite.Litestream"], [SceneServer]),
        new("snapshots", RightRailX, 560, RailW, 52, "…Snapshots", "point in time",
            "ops", ["Rask.SQLite.Snapshots"], []),
        new("webpush", RightRailX, 628, RailW, 52, "Rask.WebPush", "VAPID, no service",
            "ops", ["Rask.WebPush"], [SceneDurable]),
        new("signaling", RightRailX, 692, RailW, 52, "Rask.Signaling", "WebRTC relay",
            "ops", ["Rask.Signaling"], []),
    ];

    /// <summary>Every box on the map, in one sequence.</summary>
    /// <remarks>
    ///     Private, and <see cref="CoveredPackages" /> is what the coverage test consumes: the test needs
    ///     the package names, not the geometry, and keeping <c>Node</c> private stops the layout table
    ///     from becoming something a test can pin.
    /// </remarks>
    private static IEnumerable<Node> AllNodes() => SpineNodes.Concat(BuildNodes).Concat(OpsNodes);

    /// <summary>The <c>src/</c> projects the diagram claims to cover.</summary>
    /// <remarks>Exposed for the coverage test, which compares it against the directories on disk.</remarks>
    internal static IReadOnlyCollection<string> CoveredPackages() =>
        AllNodes().SelectMany(n => n.Packages).Distinct().ToArray();

    // ---- the front-end lane: the choice a reader arrives with ----
    //
    // Three tiers, and the tier a framework sits in is the whole lesson. A foreign-runtime island owns
    // its own DOM, so Rask marks the subtree opaque and the differ refuses to descend. A Blazor island
    // is the opposite: Rask renders it to HTML itself, so nothing in the browser owns those nodes and
    // the subtree is diffed like any other markup. The SPA lane has no Rask runtime at all.
    private readonly record struct Chip(string Label, string Kind);

    private static readonly Chip[] IslandChips =
    [
        new("React", "chip"),
        new("Preact", "chip"),
        new("Lit", "chip"),
        // Landing, not landed: these two are still on a branch. Dashed until they merge.
        new("Vue", "chip-soon"),
        new("Svelte", "chip-soon"),
        // Shipped, and marked differently because it is a different KIND, not a different vendor:
        // Rask renders the .razor component itself, so its subtree is diffed rather than left opaque.
        new("Blazor", "chip-blazor"),
    ];

    // Exactly the list `rask new --template …` advertises. A template added without a chip here fails
    // the site's own test rather than quietly going undocumented.
    private static readonly string[] SpaTemplates =
        ["React", "Preact", "Vue", "Solid", "Svelte", "Lit", "Angular"];

    /// <summary>The SPA templates the diagram shows, for the test that pins them to the CLI's list.</summary>
    internal static IReadOnlyList<string> SpaTemplateChips() => SpaTemplates;
}
