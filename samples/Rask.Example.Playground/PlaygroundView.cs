using System.Globalization;
using System.Text;
using Microsoft.JSInterop;
using Rask.Bootstrap;
using Rask.Core.Components;
using Rask.Example.Playground.Compiler;
using Rask.Html.Components;

namespace Rask.Example.Playground;

// The whole playground UI: an example gallery on the left, a code editor in the middle, and — on the right —
// the live-rendered result of compiling that code plus any compiler/analyzer diagnostics. The compiled
// component is mounted as a child of THIS component's tree (inside an ErrorBoundary), so it shares this app's
// single WasmLiveSession: its event handlers, state and live diffing all work with no extra wiring.
//
// The editor is a real IDE: once the framework references finish downloading in the background, a
// PlaygroundWorkspace drives as-you-type Roslyn diagnostics (squiggles before you ever press Run) and
// IntelliSense — wired to Monaco through PlaygroundLanguageInterop's static [JSInvokable]s. Pressing Run is
// the only path that Emits + loads an assembly (see PlaygroundCompiler); typing never does, so it can't leak.
//
// Scoped assets: PlaygroundView.js (editor mount + value, language-feature registration, framework-assembly
// discovery) and PlaygroundView.css (layout + editor-style IDE chrome).
public sealed partial class PlaygroundView : Component
{
    private readonly IJSRuntime _js;
    private readonly WasmReferenceLoader _loader;
    private readonly IServiceProvider _services;

    // Field so its DOM element stays stable across renders — the host the Monaco editor mounts into.
    private readonly ElementRef _editorHost = ElementRef.New();

    private PlaygroundCompiler? _compiler;
    private PlaygroundWorkspace? _workspace;
    private PlaygroundResult? _result;
    private string _phase = "Loading editor…";
    private bool _busy;

    // Which gallery example is loaded — drives the active-item highlight and what Reset restores.
    private string _activeSampleId = PlaygroundSamples.All[0].Id;

    // The left pane shows one of two lists. The playground opens on the gallery (an editor with something
    // runnable in it), with the guided track one click away.
    private PlaygroundTab _tab = PlaygroundTab.Examples;

    // Which chapter is loaded, and which ones have compiled at least once this session. Progress is
    // deliberately in-memory only: a reload is a clean slate, like the chapter databases themselves.
    private string _activeChapterId = TutorialChapters.First.Id;
    private readonly HashSet<string> _completedChapters = new(StringComparer.Ordinal);

    // Whether this build ships the EF Core + SQLite reference set (see RaskPlaygroundData in the csproj).
    // The fast no-native build doesn't, and the data chapters say so rather than failing to compile.
    private const bool DataChaptersAvailable =
#if RASK_PLAYGROUND_DATA
        true;
#else
        false;
#endif

    // Progress of the background reference download that powers the IDE features (diagnostics + IntelliSense).
    private IdeState _ide = IdeState.Loading;

    // Set once the editor (Monaco, or the textarea fallback) has mounted. Every control is disabled until
    // then (see CanInteract) so a click can't read an empty editor and compile nothing, or push code into
    // an editor that doesn't exist yet — and Playwright's actionability wait means the E2E naturally waits
    // for the editor without a bespoke sleep.
    private bool _editorReady;

    // Bumps every compile so the preview subtree (and its ErrorBoundary) is keyed fresh — a new run mounts
    // a clean component instance and clears any tripped boundary from the previous run.
    private int _runId;

    public PlaygroundView(IJSRuntime js, WasmReferenceLoader loader, IServiceProvider services)
    {
        _js = js;
        _loader = loader;
        _services = services;
    }

    // The Rask brand mark (violet-gradient bolt), matching the marketing site + docs. Inlined here
    // because RaskLogo lives in Rask.Example.Shared, which the isolated playground doesn't reference.
    private const string BoltSvg =
        "<svg viewBox=\"0 0 128 128\" aria-hidden=\"true\" class=\"pg-mark\"><defs>" +
        "<linearGradient id=\"pgb\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
        "<stop offset=\"0\" stop-color=\"#8b5cf6\"/><stop offset=\"1\" stop-color=\"#7c3aed\"/></linearGradient></defs>" +
        "<rect width=\"128\" height=\"128\" rx=\"28\" fill=\"url(#pgb)\"/>" +
        "<path d=\"M74 24 L38 66 L58 66 L53 104 L92 58 L70 58 Z\" fill=\"#fff\"/></svg>";

    private static readonly IReadOnlyDictionary<string, string?> ThemeToggleAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Toggle light / dark theme" };

    // Flip the color theme (data-theme + data-bs-theme on <html>, persisted to localStorage so it carries
    // across the site/docs/playground). Client-only; a torn-down transport just no-ops.
    private async Task ToggleThemeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("Rask.PlaygroundView.toggleTheme");
        }
        catch (JSDisconnectedException)
        {
            // Nothing to toggle on a gone transport.
        }
    }

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // The editor host div now exists in the DOM; create the Monaco editor inside it. mountEditor never
        // throws (it falls back to a textarea), so reaching here means the editor is usable.
        //
        // The timeout covers the case that promise never SETTLES, which is different from failing: if the
        // scoped PlaygroundView.js module didn't load at all, there is nothing to resolve the invoke, so
        // without a deadline _editorReady stays false forever and every control sits disabled with no
        // explanation. That has happened for real — a build that silently baked no scoped assets — and it
        // reads as "the playground is broken", sending you to debug Roslyn or Monaco rather than the build
        // (#650). Generous, because a slow connection fetching Monaco must not trip it.
        try
        {
            await _js.InvokeVoidAsync(
                "Rask.PlaygroundView.mountEditor", TimeSpan.FromSeconds(60), _editorHost,
                PlaygroundSamples.Starter);
            _editorReady = true;
            _phase = "Press Run to compile.";
        }
        catch (Exception ex) when (ex is TaskCanceledException or JSException or JSDisconnectedException)
        {
            // Everything stays disabled — without the module we cannot even read the editor's contents —
            // but now it says why instead of looking like a hung page.
            _phase = "The editor module (PlaygroundView.js) did not load — try reloading the page.";
            _ide = IdeState.Unavailable;
            return;
        }

        // Kick off the (multi-MB) reference download in the background so IntelliSense + live diagnostics come
        // alive a few seconds after load — without blocking first paint or the first Run. Fire-and-forget:
        // it re-renders itself via StateHasChanged when the IDE state changes (the LiveTicker pattern).
        _ = InitLanguageServicesAsync();
    }

    private async Task InitLanguageServicesAsync()
    {
        try
        {
            var references = await _loader.LoadAsync().ConfigureAwait(false);
            _compiler ??= new PlaygroundCompiler(references, _services);
            _workspace = new PlaygroundWorkspace(references);

            // Publish the engine to the static bridge Monaco calls into, then register the providers.
            PlaygroundLanguageInterop.Workspace = _workspace;
            await _js.InvokeVoidAsync("Rask.PlaygroundView.registerLanguageFeatures", _editorHost)
                .ConfigureAwait(false);

            _ide = IdeState.Ready;
        }
        catch
        {
            // IDE features are a bonus — a failed/blocked download just means no live squiggles/completions.
            // Compile-on-Run still works (RunAsync loads the references itself if needed).
            _ide = IdeState.Unavailable;
        }

        StateHasChanged();
    }

    protected override void OnUnmount()
    {
        // Drop the static back-reference so a torn-down editor can't be queried, and release the workspace.
        if (ReferenceEquals(PlaygroundLanguageInterop.Workspace, _workspace))
        {
            PlaygroundLanguageInterop.Workspace = null;
        }

        _workspace?.Dispose();
    }

    protected override Component? Render() =>
        Div.Class("pg")[
            Header.Class("pg-bar")[
                Div.Class("pg-brand")[
                    Raw.Value(BoltSvg),
                    Span.Class("pg-title")["Rask Playground"],
                    IdeBadge()
                ],
                Div.Class("pg-actions")[
                    Span.Class("pg-phase")[_phase],
                    // Reset / Run — the same Bs* button language as the docs; the pg-run class stays a hook
                    // for the Ctrl/Cmd+Enter shortcut (PlaygroundView.js) and the E2E.
                    BsButton
                        .Class("pg-reset")
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Disabled(!CanInteract)
                        .OnClickAsync(ResetAsync)["Reset"],
                    BsButton
                        .Class("pg-run")
                        .Color(BsColor.Primary)
                        .Size(BsSize.Sm)
                        .Disabled(!CanInteract || IsActiveChapterLocked)
                        .OnClickAsync(RunAsync)[_busy ? "Running…" : "Run ▸"],
                    // Cross-app links back to the docs + repo, and the shared light/dark toggle.
                    BsLink
                        .Href("https://pal-tamas.github.io/rask/docs/")
                        .Target("_blank")
                        .Rel("noopener")
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)[
                        BsIcon.Name(BsIconName.Book).Class("me-1"), "Docs"],
                    BsLink
                        .Href("https://github.com/pal-tamas/rask")
                        .Target("_blank")
                        .Rel("noopener")
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)[
                        BsIcon.Name(BsIconName.Github).Class("me-1"), "GitHub"],
                    BsButton
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .OnClickAsync(ToggleThemeAsync)
                        .Aria(ThemeToggleAria)[BsIcon.Name(BsIconName.CircleHalf)]
                ]
            ],
            Div.Class("pg-body")[
                Aside.Class("pg-examples")[
                    Div.Class("pg-tabs")[
                        TabButton(PlaygroundTab.Tutorial, "Tutorial"),
                        TabButton(PlaygroundTab.Examples, "Examples")
                    ],
                    _tab == PlaygroundTab.Tutorial ? ChapterList() : SampleList()
                ],
                Section.Class("pg-editor")[
                    // The brief is ALWAYS rendered, empty when the gallery is showing (CSS hides an empty
                    // one). That is load-bearing, not laziness: it keeps the editor host at a fixed child
                    // slot. Rendering the brief conditionally would shift the host's position on every tab
                    // switch, and the positional diff would then match the brief against the live Monaco
                    // host — rewriting its attributes and inserting the brief's content into the DOM Monaco
                    // owns. Keying the two children instead does NOT work: a keyed host is re-created by
                    // the full-document morph, which orphans the editor Monaco mounted into the old node
                    // (the editor keeps rendering, detached, and setEditorValue silently stops landing).
                    Brief(),
                    // Monaco mounts into this host in OnRenderedAsync(firstRender). The host renders childless,
                    // so the positional diff never addresses inside it — but a *morph* (every full-HTML frame)
                    // compares live children against the rendered ones and would strip Monaco's DOM. mountEditor
                    // tags the nodes Monaco creates with data-rask-managed, which takes them out of the live-side
                    // comparison; the marker belongs on those library-created children, never on this host.
                    Div.Ref(_editorHost).Class("pg-code-host")
                ],
                Section.Class("pg-output")[
                    Div.Class("pg-preview-head")["Preview"],
                    Div.Class("pg-preview").Key(_runId)[PreviewBody()],
                    Diagnostics()
                ]
            ]
        ];

    private Component TabButton(PlaygroundTab tab, string label) =>
        Button
            .Id(TutorialPaneState.TabId(tab))
            .Class(_tab == tab
                ? $"{TutorialPaneState.TabClass} {TutorialPaneState.Active}"
                : TutorialPaneState.TabClass)
            .Disabled(!CanInteract)
            .OnClickAsync(() => SwitchTabAsync(tab))[label];

    // Switching tabs loads what that tab is pointing at, so the editor always holds the thing the pane
    // highlights. Without this the brief could describe chapter 3 while the editor held a gallery sample —
    // and Run would then tick chapter 3 off for compiling something else entirely.
    private Task SwitchTabAsync(PlaygroundTab tab)
    {
        if (_busy || _tab == tab)
        {
            return Task.CompletedTask;
        }

        return tab == PlaygroundTab.Tutorial
            ? SelectChapterAsync(ActiveChapter)
            : SelectSampleAsync(PlaygroundSamples.All.First(s => s.Id == _activeSampleId));
    }

    private Component SampleList() =>
        Nav.Class("pg-example-list")[
            PlaygroundSamples.All.Select(s =>
                Button
                    .Key(s.Id)
                    .Class(s.Id == _activeSampleId ? "pg-example is-active" : "pg-example")
                    .Disabled(!CanInteract)
                    .OnClickAsync(() => SelectSampleAsync(s))[
                    Span.Class("pg-example-title")[s.Title],
                    Span.Class("pg-example-blurb")[s.Blurb]
                ])
        ];

    private Component ChapterList() =>
        Nav.Class("pg-chapter-list")[
            TutorialChapters.All.Select(c =>
                Button
                    .Key(c.Id)
                    .Id(TutorialPaneState.ChapterId(c.Number))
                    .Class(TutorialPaneState.ClassesFor(StateOf(c), _completedChapters.Contains(c.Id)))
                    .Disabled(!CanInteract)
                    .OnClickAsync(() => SelectChapterAsync(c))[
                    Span.Class("pg-chapter-no")[c.Number.ToString(CultureInfo.InvariantCulture)],
                    Span.Class("pg-chapter-title")[c.Title],
                    Span.Class("pg-chapter-goal")[c.Goal]
                ])
        ];

    private ChapterState StateOf(TutorialChapter chapter)
    {
        if (chapter.NeedsDatabase && !DataChaptersAvailable)
        {
            return ChapterState.Locked;
        }

        return chapter.Id == _activeChapterId && _tab == PlaygroundTab.Tutorial
            ? ChapterState.Active
            : ChapterState.Open;
    }

    // The one gate every control shares: nothing may be clicked while a compile is in flight, and nothing
    // may be clicked before the editor exists.
    //
    // The second half is the subtle one and used to be missing from the selection controls (#647). Loading
    // a chapter or an example is a JS round-trip to setEditorValue, and before mountEditor has run there is
    // no editor registered for the host, so that call is a silent no-op — mountEditor then finishes and
    // installs the starter over the selection. The reader was left with the brief and the highlight showing
    // one chapter while the editor held another, and Run cheerfully compiled the wrong code and ticked the
    // chapter off. On a cold load the window is seconds wide, which is exactly when a first-time reader
    // clicks "Tutorial".
    //
    // One property rather than six copies of the condition: the bug was two of those copies disagreeing.
    private bool CanInteract => !_busy && _editorReady;

    // True when the editor holds a chapter this build can't compile. Run is disabled rather than left to
    // fill the preview with CS0246s about DbContext — the reader can still read the code.
    private bool IsActiveChapterLocked =>
        _tab == PlaygroundTab.Tutorial && ActiveChapter.NeedsDatabase && !DataChaptersAvailable;

    // The instruction band above the editor: what this chapter is for, what to notice, and the way on.
    // Always rendered — empty on the Examples tab, where CSS collapses it — so the editor host below keeps
    // a fixed child slot. See the note at the call site.
    private Component Brief()
    {
        if (_tab != PlaygroundTab.Tutorial)
        {
            return Div.Class("pg-brief");
        }

        var chapter = ActiveChapter;
        var locked = chapter.NeedsDatabase && !DataChaptersAvailable;

        return Div.Class("pg-brief")[
            Div.Class("pg-brief-head")[
                Span.Class("pg-brief-title")[
                    $"Chapter {chapter.Number.ToString(CultureInfo.InvariantCulture)} — {chapter.Title}"
                ],
                Div.Class("pg-brief-nav")[
                    BsButton
                        .Class("pg-prev")
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Disabled(!CanInteract || chapter.Number == 1)
                        .OnClickAsync(() => StepAsync(-1))["← Back"],
                    BsButton
                        .Class("pg-next")
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Disabled(!CanInteract || chapter.Number == TutorialChapters.All.Count)
                        .OnClickAsync(() => StepAsync(1))["Next →"]
                ]
            ],
            P.Class("pg-brief-goal")[chapter.Goal],
            locked
                ? P.Class("pg-brief-locked")[
                    "This build ships without the EF Core + SQLite reference set, so this chapter can be "
                    + "read but not run. The deployed playground has it."
                ]
                : null,
            Ul.Class("pg-brief-steps")[
                chapter.Steps.Select((s, i) => Li.Key(i)[s])
            ]
        ];
    }

    private TutorialChapter ActiveChapter =>
        TutorialChapters.All.FirstOrDefault(c => c.Id == _activeChapterId) ?? TutorialChapters.First;

    private Component? IdeBadge()
    {
        var (color, text) = _ide switch
        {
            IdeState.Ready => (BsColor.Success, "IntelliSense ready"),
            IdeState.Unavailable => (BsColor.Danger, "IntelliSense unavailable"),
            _ => (BsColor.Secondary, "Loading IntelliSense…")
        };

        // The state rides a class as well as the colour and the label: it is what the E2E waits on to know
        // the workspace has its references, and a colour is not a thing a locator can wait for. See
        // IdeBadgeState — the mapping is pinned by a unit test precisely because losing it fails as a
        // three-minute Playwright timeout rather than as anything that names the cause (#593).
        return BsBadge.Color(color).Pill(true).Class($"pg-ide {IdeBadgeState.ClassFor(_ide)}")[text];
    }

    private Component PreviewBody()
    {
        if (_result is { Succeeded: true, Component: { } component })
        {
            // The compiled component runs inside a boundary so a throwing Render() shows a message instead
            // of blanking the playground. Keyed via the parent container so each run is a fresh mount.
            //
            // Mount() is what gives it a lifecycle. It was built by ActivatorUtilities, not by a generated
            // factory, so nothing has adopted it: handed straight to the boundary it renders fine but never
            // receives OnMount/OnMountAsync, which is silent and looks exactly like code that doesn't work —
            // a component that loads in OnMountAsync just sits on its placeholder.
            return ErrorBoundary.Fallback(RenderPreviewError)[Mount.Child(component)];
        }

        return Div.Class("pg-preview-empty")[
            _result is null
                ? "Your component renders here."
                : "Fix the errors below to see the preview."
        ];
    }

    private static Component RenderPreviewError(Exception error, Action recover) =>
        Div.Class("pg-preview-error")[
            Strong["The component threw while rendering:"],
            // The whole chain, not just the outermost message. The ones that matter most here say the least
            // on their own: EF Core's "An error occurred while saving the entity changes. See the inner
            // exception for details." is a pointer to the message the reader actually needs.
            Pre[ExceptionChain(error)],
            Button.Class("pg-retry").OnClick(recover)["Retry"]
        ];

    private static string ExceptionChain(Exception error)
    {
        var text = new StringBuilder();

        for (var current = error; current is not null; current = current.InnerException)
        {
            if (text.Length > 0)
            {
                text.Append("\n → ");
            }

            text.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return text.ToString();
    }

    private Component? Diagnostics()
    {
        if (_result is null || _result.Diagnostics.Count == 0)
        {
            return null;
        }

        return Div.Class("pg-diagnostics")[
            _result.Diagnostics.Select((d, i) =>
                Div.Key(i).Class($"pg-diag pg-diag-{Severity(d.Severity)}")[
                    Span.Class("pg-diag-id")[d.Id],
                    Span.Class("pg-diag-loc")[$"({d.StartLine},{d.StartColumn})"],
                    Span.Class("pg-diag-msg")[d.Message]
                ])
        ];
    }

    // Load a gallery example into the editor: swap the code, clear the previous preview. The editor's own
    // change event refreshes the live diagnostics, so there's nothing else to trigger.
    private async Task SelectSampleAsync(PlaygroundSample sample)
    {
        if (_busy)
        {
            return;
        }

        _tab = PlaygroundTab.Examples;
        _activeSampleId = sample.Id;
        _result = null;
        _runId++;
        _phase = "Press Run to compile.";
        await _js.InvokeVoidAsync("Rask.PlaygroundView.setEditorValue", _editorHost, sample.Code);
    }

    // Load a tutorial chapter into the editor. Same swap as a gallery example, plus the pane state that
    // makes this chapter the active one.
    private async Task SelectChapterAsync(TutorialChapter chapter)
    {
        if (_busy)
        {
            return;
        }

        _tab = PlaygroundTab.Tutorial;
        _activeChapterId = chapter.Id;
        _result = null;
        _runId++;
        _phase = chapter.NeedsDatabase && !DataChaptersAvailable
            ? "This build ships without SQLite — read-only."
            : "Press Run to compile.";
        await _js.InvokeVoidAsync("Rask.PlaygroundView.setEditorValue", _editorHost, chapter.Code);
    }

    private Task StepAsync(int delta)
    {
        var next = ActiveChapter.Number - 1 + delta;
        return next >= 0 && next < TutorialChapters.All.Count
            ? SelectChapterAsync(TutorialChapters.All[next])
            : Task.CompletedTask;
    }

    // Restore what's loaded to its original code — the tab decides, and the tab is authoritative because
    // switching it loads that pane's code (see SwitchTabAsync). In the tutorial, also drop THIS chapter's
    // database, so "Reset" is a genuine clean slate rather than code-clean-but-rows-still-there. Only this
    // chapter's: wiping the others would throw away a neighbouring chapter's state the reader still wants.
    private Task ResetAsync()
    {
        if (_tab == PlaygroundTab.Tutorial)
        {
            var chapter = ActiveChapter;
            DeleteChapterDatabase(chapter);
            return SelectChapterAsync(chapter);
        }

        return SelectSampleAsync(PlaygroundSamples.All.First(s => s.Id == _activeSampleId));
    }

    // The chapters address their databases by relative path, so they live in the runtime's working
    // directory — an in-memory filesystem in the browser. Plain BCL file IO: no EF Core reference needed
    // here, which is what keeps the host code buildable on a build without the data packages.
    private static void DeleteChapterDatabase(TutorialChapter chapter)
    {
        if (!chapter.NeedsDatabase)
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(
                Directory.GetCurrentDirectory(),
                $"ch{chapter.Number.ToString(CultureInfo.InvariantCulture)}.db"));
        }
        catch (IOException)
        {
            // A database that won't delete just means the next run reuses it — not worth failing Reset over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    private async Task RunAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            // No ConfigureAwait(false) in this handler: staying on the framework's handler sync context is
            // what makes each `_phase = …` before an await paint mid-handler (the "Downloading…/Compiling…"
            // window), and shows the disabled "Running…" button — derived UI updating with no manual
            // StateHasChanged.
            var code = await _js.InvokeAsync<string>("Rask.PlaygroundView.editorValue", _editorHost);

            if (_compiler is null)
            {
                // One-time: download the shipped framework assemblies as Roslyn references (several MB). Shares
                // the loader cache with the background IDE init, so this never double-downloads.
                _phase = "Downloading compiler…";
                var references = await _loader.LoadAsync();
                _compiler = new PlaygroundCompiler(references, _services);
            }

            _phase = "Compiling…";
            _runId++;
            _result = await _compiler.CompileAsync(code);
            _phase = _result.Succeeded
                ? "Compiled ✓"
                : $"{_result.Diagnostics.Count(d => d.Severity == PlaygroundSeverity.Error)} error(s)";

            // Tick the chapter off once it has actually compiled — the reader's own edits count, so this
            // marks "I got this to build", not "I clicked it".
            if (_result.Succeeded && _tab == PlaygroundTab.Tutorial)
            {
                _completedChapters.Add(_activeChapterId);
            }

            // Paint the compiler/analyzer diagnostics as inline editor squiggles (best-effort).
            await _js.InvokeVoidAsync("Rask.PlaygroundView.setMarkers", _editorHost,
                PlaygroundLanguageInterop.SerializeDiagnostics(_result.Diagnostics));
        }
        catch (Exception ex)
        {
            _phase = "Playground error: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static string Severity(PlaygroundSeverity severity) => severity switch
    {
        PlaygroundSeverity.Error => "error",
        PlaygroundSeverity.Warning => "warn",
        _ => "info"
    };
}
