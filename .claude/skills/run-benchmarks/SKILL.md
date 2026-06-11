---
name: run-benchmarks
description: Run Rask render/runtime benchmarks before and after a framework change and report the Allocated delta. Use whenever you modify render hot-path or live-runtime code in src/Rask.Core or src/Rask.Server (HtmlSerializer, Element/Component render, Live/* diff codec, WS/WASM dispatch, payload build). Required evidence for any hotpath PR.
---

# run-benchmarks

BenchmarkDotNet, Release only, hand-run (not part of `dotnet test`). Project:
`benchmarks/Rask.Benchmarks/Rask.Benchmarks.csproj`. All benches use `[MemoryDiagnoser]`.

## 1. Pick the bench class matching the change
| Area changed | Bench class (`--filter "*Name*"`) |
|---|---|
| End-to-end HTML render + WS payload | `RenderRoundTripBenchmarks`, `LiveRenderRoundTripBenchmarks` |
| Payload build / body inject/extract | `LivePayloadUtf8Benchmarks` |
| Diff DOM-walk | `FrameDifferBenchmarks` |
| Attribute encoding | `AttributeEncodingBenchmarks` |
| WS / WASM dispatch | `WsDispatchBenchmarks`, `WasmDispatchBenchmarks` |
| Asset / download | `AssetLoadingBenchmarks`, `DownloadPayloadBenchmarks` |

## 2. Baseline (pre-change) then post-change
```bash
# capture baseline on the unchanged tree
git stash                      # or check out the parent commit
dotnet run -c Release --project benchmarks/Rask.Benchmarks/Rask.Benchmarks.csproj --filter "*RenderRoundTrip*"
# results land in benchmarks/Rask.Benchmarks/BenchmarkDotNet.Artifacts/results/*-report-github.md
git stash pop                  # restore the change, re-run the same filter
dotnet run -c Release --project benchmarks/Rask.Benchmarks/Rask.Benchmarks.csproj --filter "*RenderRoundTrip*"
```

## 3. Compare + report
Read the `Allocated` (and `Mean`) columns from the two
`BenchmarkDotNet.Artifacts/results/*-report-github.md` runs. Trust `Allocated` even on
`InvocationCount=1` benches. Quote the **delta** (e.g. "Allocated 1.84 KB → 0.91 KB, −51%") in
the PR body. Custom byte reports: append `bundle-size` or `payload-bytes` instead of `--filter`.
