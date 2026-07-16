using Microsoft.JSInterop;
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
// discovery) and PlaygroundView.css (layout + Rails-ish IDE chrome).
public sealed class PlaygroundView : Component
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

    private enum IdeState
    {
        Loading,
        Ready,
        Unavailable
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
                    Span(Class: "pg-bolt")["⚡"],
                    Span(Class: "pg-title")["Rask Playground"],
                    IdeBadge()
                ],
                Div(Class: "pg-actions")[
                    Span(Class: "pg-phase")[_phase],
                    Button(
                        Class: "pg-reset",
                        Disabled: _busy || !_editorReady,
                        OnClickAsync: ResetAsync)["Reset"],
                    Button(
                        Class: _busy ? "pg-run is-busy" : "pg-run",
                        Disabled: _busy || !_editorReady,
                        OnClickAsync: RunAsync)[_busy ? "Running…" : "Run ▸"]
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
        var (cls, text) = _ide switch
        {
            IdeState.Ready => ("pg-ide is-ready", "IntelliSense ready"),
            IdeState.Unavailable => ("pg-ide is-off", "IntelliSense unavailable"),
            _ => ("pg-ide is-loading", "Loading IntelliSense…")
        };

        return Span(Class: cls)[text];
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
