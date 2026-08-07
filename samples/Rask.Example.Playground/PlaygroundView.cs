using Microsoft.JSInterop;
using Rask.Bootstrap;
using Rask.Core.Components;
using Rask.Example.Playground.Compiler;

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

    // Progress of the background reference download that powers the IDE features (diagnostics + IntelliSense).
    private IdeState _ide = IdeState.Loading;

    // Set once the editor (Monaco, or the textarea fallback) has mounted. Run stays disabled until then so
    // a click can't read an empty editor and compile nothing — and Playwright's actionability wait means
    // the E2E naturally waits for the editor without a bespoke sleep.
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
        await _js.InvokeVoidAsync("Rask.PlaygroundView.mountEditor", _editorHost, PlaygroundSamples.Starter);
        _editorReady = true;
        _phase = "Press Run to compile.";

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
        Div(Class: "pg")[
            Header(Class: "pg-bar")[
                Div(Class: "pg-brand")[
                    Raw(BoltSvg),
                    Span(Class: "pg-title")["Rask Playground"],
                    IdeBadge()
                ],
                Div(Class: "pg-actions")[
                    Span(Class: "pg-phase")[_phase],
                    // Reset / Run — the same Bs* button language as the docs; the pg-run class stays a hook
                    // for the Ctrl/Cmd+Enter shortcut (PlaygroundView.js) and the E2E.
                    BsButton(Class: "pg-reset", Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                        Disabled: _busy || !_editorReady, OnClickAsync: ResetAsync)["Reset"],
                    BsButton(Class: "pg-run", Color: BsColor.Primary, Size: BsSize.Sm,
                        Disabled: _busy || !_editorReady, OnClickAsync: RunAsync)[_busy ? "Running…" : "Run ▸"],
                    // Cross-app links back to the docs + repo, and the shared light/dark toggle.
                    BsLink(Href: "https://pal-tamas.github.io/rask/docs/", Target: "_blank", Rel: "noopener",
                        Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm)[
                        BsIcon(Name: BsIconName.Book, Class: "me-1"), "Docs"],
                    BsLink(Href: "https://github.com/pal-tamas/rask", Target: "_blank", Rel: "noopener",
                        Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm)[
                        BsIcon(Name: BsIconName.Github, Class: "me-1"), "GitHub"],
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                        OnClickAsync: ToggleThemeAsync, Aria: ThemeToggleAria)[BsIcon(Name: BsIconName.CircleHalf)]
                ]
            ],
            Div(Class: "pg-body")[
                Aside(Class: "pg-examples")[
                    Div(Class: "pg-examples-head")["Examples"],
                    Nav(Class: "pg-example-list")[
                        PlaygroundSamples.All.Select(s =>
                            Button(
                                Key: s.Id,
                                Class: s.Id == _activeSampleId ? "pg-example is-active" : "pg-example",
                                Disabled: _busy,
                                OnClickAsync: () => SelectSampleAsync(s))[
                                Span(Class: "pg-example-title")[s.Title],
                                Span(Class: "pg-example-blurb")[s.Blurb]
                            ])
                    ]
                ],
                Section(Class: "pg-editor")[
                    // Monaco mounts into this host in OnRenderedAsync(firstRender). The host renders childless,
                    // so the positional diff never addresses inside it — but a *morph* (every full-HTML frame)
                    // compares live children against the rendered ones and would strip Monaco's DOM. mountEditor
                    // tags the nodes Monaco creates with data-rask-managed, which takes them out of the live-side
                    // comparison; the marker belongs on those library-created children, never on this host.
                    Div(Ref: _editorHost, Class: "pg-code-host")
                ],
                Section(Class: "pg-output")[
                    Div(Class: "pg-preview-head")["Preview"],
                    Div(Class: "pg-preview", Key: _runId)[PreviewBody()],
                    Diagnostics()
                ]
            ]
        ];

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
        return BsBadge(Color: color, Pill: true, Class: $"pg-ide {IdeBadgeState.ClassFor(_ide)}")[text];
    }

    private Component PreviewBody()
    {
        if (_result is { Succeeded: true, Component: { } component })
        {
            // The compiled component runs inside a boundary so a throwing Render() shows a message instead
            // of blanking the playground. Keyed via the parent container so each run is a fresh mount.
            return ErrorBoundary(Fallback: RenderPreviewError)[component];
        }

        return Div(Class: "pg-preview-empty")[
            _result is null
                ? "Your component renders here."
                : "Fix the errors below to see the preview."
        ];
    }

    private static Component RenderPreviewError(Exception error, Callback recover) =>
        Div(Class: "pg-preview-error")[
            Strong()["The component threw while rendering:"],
            Pre()[error.Message],
            Button(Class: "pg-retry", OnClick: recover)["Retry"]
        ];

    private Component? Diagnostics()
    {
        if (_result is null || _result.Diagnostics.Count == 0)
        {
            return null;
        }

        return Div(Class: "pg-diagnostics")[
            _result.Diagnostics.Select((d, i) =>
                Div(Key: i, Class: $"pg-diag pg-diag-{Severity(d.Severity)}")[
                    Span(Class: "pg-diag-id")[d.Id],
                    Span(Class: "pg-diag-loc")[$"({d.StartLine},{d.StartColumn})"],
                    Span(Class: "pg-diag-msg")[d.Message]
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

        _activeSampleId = sample.Id;
        _result = null;
        _runId++;
        _phase = "Press Run to compile.";
        await _js.InvokeVoidAsync("Rask.PlaygroundView.setEditorValue", _editorHost, sample.Code);
    }

    // Restore the active example's original code (after the visitor has edited it).
    private Task ResetAsync() =>
        SelectSampleAsync(PlaygroundSamples.All.First(s => s.Id == _activeSampleId));

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
