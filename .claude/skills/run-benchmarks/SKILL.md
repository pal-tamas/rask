---
name: run-benchmarks
description: Run Rask render/runtime benchmarks before and after a framework change and report the Allocated delta. Use whenever you modify render hot-path or live-runtime code in src/Rask.Core or src/Rask.Server (HtmlSerializer, Element/Component render, Live/* diff codec, handler dispatch, a session's render-and-send, payload build). Required evidence for any hotpath PR.
---

# run-benchmarks

BenchmarkDotNet, Release only, hand-run (not part of `dotnet test`). Project:
`tests/Rask.Benchmarks/Rask.Benchmarks.csproj`. All benches use `[MemoryDiagnoser]`.

## 1. Pick the bench class matching the change
| Area changed | Bench class (`--filter "*Name*"`) |
|---|---|
| End-to-end HTML render + WS payload | `RenderRoundTripBenchmarks`, `LiveRenderRoundTripBenchmarks` |
| A session's state change → render → diff-or-full → send | `LiveSessionSendBenchmarks` (and `LiveTransportSendBenchmarks` for the transport seam) |
| Payload build / body inject/extract | `LivePayloadUtf8Benchmarks` |
| Diff DOM-walk | `FrameDifferBenchmarks` |
| Attribute encoding | `AttributeEncodingBenchmarks` |
| Handler invocation (`Component.TryInvokeHandlerAsync`, every host's dispatch) | `HandlerDispatchBenchmarks` |
| Inbound frame parsing only (WS / WASM) | `WsDispatchBenchmarks`, `WasmDispatchBenchmarks` — they parse JSON and call no handler, so they cannot show a dispatch change |
| Asset / download | `AssetLoadingBenchmarks`, `DownloadPayloadBenchmarks` |

## 2. Baseline (pre-change) then post-change
```bash
# capture baseline on the unchanged tree (a WIP commit, or check out the parent — never a bare `git stash`,
# which is shared with every other worktree)
dotnet run -c Release --project tests/Rask.Benchmarks/Rask.Benchmarks.csproj -- --filter "*RenderRoundTrip*" \
  --artifacts artifacts/bench/before
# restore the change, re-run the same filter into its own folder
dotnet run -c Release --project tests/Rask.Benchmarks/Rask.Benchmarks.csproj -- --filter "*RenderRoundTrip*" \
  --artifacts artifacts/bench/after
```

BenchmarkDotNet writes relative to the WORKING DIRECTORY, not the project: run from the repo root without
`--artifacts` and results land in `./BenchmarkDotNet.Artifacts/results/`, not under `tests/Rask.Benchmarks/`.
Naming the folder keeps the two runs apart and out of the tree's root. Run it from inside the repo either way —
started elsewhere, BenchmarkDotNet cannot find the project to build its runner.

## 3. Compare + report
Read the `Allocated` (and `Mean`) columns from the two
`artifacts/bench/{before,after}/results/*-report-github.md` runs. Trust `Allocated` even on
`InvocationCount=1` benches. Quote the **delta** (e.g. "Allocated 1.84 KB → 0.91 KB, −51%") in
the PR body. Custom reports: append `bundle-size`, `payload-bytes`, `session-footprint` (per-live-session retained
memory + sessions-per-GiB across a page-size sweep) or `session-churn` (soak + create/dispose churn)
instead of `--filter`.
