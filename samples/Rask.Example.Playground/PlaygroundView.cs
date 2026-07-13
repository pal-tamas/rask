using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core.Components;
using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground;

// The whole playground UI: a code editor on the left, and — on the right — the live-rendered result of
// compiling that code plus any compiler/analyzer diagnostics. The compiled component is mounted as a
// child of THIS component's tree (inside an ErrorBoundary), so it shares this app's single WasmLiveSession:
// its event handlers, state and live diffing all work with no extra wiring. Scoped assets: PlaygroundView.js
// (editor value + framework-assembly discovery) and PlaygroundView.css (layout).
public sealed class PlaygroundView : Component
{
    private readonly IJSRuntime _js;
    private readonly WasmReferenceLoader _loader;
    private readonly IServiceProvider _services;

    // Field so its DOM element stays stable across renders — the host the Monaco editor mounts into.
    private readonly ElementRef _editorHost = ElementRef.New();

    private PlaygroundCompiler? _compiler;
    private PlaygroundResult? _result;
    private string _phase = "Loading editor…";
    private bool _busy;

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

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The editor host div now exists in the DOM; create the Monaco editor inside it. mountEditor
            // never throws (it falls back to a textarea), so reaching here means the editor is usable.
            await _js.InvokeVoidAsync("Rask.PlaygroundView.mountEditor", _editorHost, PlaygroundSamples.Starter);
            _editorReady = true;
            _phase = "Press Run to compile.";
        }
    }

    protected override Component? Render() =>
        Div(Class: "pg")[
            Header(Class: "pg-bar")[
                Div(Class: "pg-brand")[
                    Span(Class: "pg-bolt")["⚡"],
                    Span(Class: "pg-title")["Rask Playground"]
                ],
                Div(Class: "pg-actions")[
                    Span(Class: "pg-phase")[_phase],
                    Button(
                        Class: _busy ? "pg-run is-busy" : "pg-run",
                        Disabled: _busy || !_editorReady,
                        OnClickAsync: RunAsync)[_busy ? "Running…" : "Run ▸"]
                ]
            ],
            Div(Class: "pg-split")[
                Section(Class: "pg-editor")[
                    // Monaco mounts into this host in OnRenderedAsync(firstRender). data-rask-managed keeps
                    // the live-diff morph from touching the editor's own DOM after mount.
                    Div(Ref: _editorHost, Class: "pg-code-host",
                        Data: new Dictionary<string, string?> { ["rask-managed"] = "" })
                ],
                Section(Class: "pg-output")[
                    Div(Class: "pg-preview-head")["Preview"],
                    Div(Class: "pg-preview", Key: _runId)[PreviewBody()],
                    Diagnostics()
                ]
            ]
        ];

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
                // One-time: download the shipped framework assemblies as Roslyn references (several MB).
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
            await _js.InvokeVoidAsync("Rask.PlaygroundView.setMarkers", _editorHost, MarkersJson(_result));
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

    private static string MarkersJson(PlaygroundResult result) =>
        JsonSerializer.Serialize(result.Diagnostics.Select(d => new
        {
            id = d.Id,
            severity = d.Severity.ToString(),
            message = d.Message,
            startLine = d.StartLine,
            startColumn = d.StartColumn,
            endLine = d.EndLine,
            endColumn = d.EndColumn
        }));

    private static string Severity(PlaygroundSeverity severity) => severity switch
    {
        PlaygroundSeverity.Error => "error",
        PlaygroundSeverity.Warning => "warn",
        _ => "info"
    };
}
