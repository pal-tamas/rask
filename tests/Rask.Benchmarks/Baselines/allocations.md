# Allocation baseline — BenchmarkDotNet

Apple M4 Pro, .NET 10.0.10, `--job short`. **Before** is `efb13c941` (2026-10-06); **now** is the tree
after that day's performance pass. Compare **Allocated**: it is deterministic. Both runs were taken on a
machine at a load average near 100, so their times are not a baseline for anything.

```bash
dotnet run -c Release --project tests/Rask.Benchmarks -- --job short --buildTimeout 1800 \
  --filter '*LiveSessionSendBenchmarks*' '*LiveRenderRoundTripBenchmarks*' '*RenderRoundTripBenchmarks*' \
  '*PageRequestBenchmarks*' '*HostDocumentRenderBenchmarks*' '*HandlerDispatchBenchmarks*' \
  '*HandlerRegistrationBenchmarks*' '*FrameDifferBenchmarks*' '*BuilderSurfaceBenchmarks*' \
  '*EventDispatchBenchmarks*'
```

| Benchmark | What it is | Before | Now |
|---|---|---:|---:|
| `LiveSessionSend.RenderAndSend` | one live update, 20-row page: render, diff, send | 8,720 B | **2,472 B** |
| `LiveSessionSend.SendOutOfBand` | a non-render frame through the same path | 0 B | 0 B |
| `HostDocumentRender.RenderAndSendWithoutToasts` | one live update of a host document | 9.36 KB | **3.23 KB** |
| `HostDocumentRender.RenderAndSendWithBuiltInToasts` | the same, with the toast region mounted | 9.43 KB | **3.33 KB** |
| `PageRequest.GetPage` | a first GET, end to end | 66.83 KB | **60.14 KB** |
| `EventDispatch.Click` (open page, 22 routes) | a click from frame bytes to its ack | 28.00 KB¹ | **18.24 KB** |
| `EventDispatch.Click` (`[Authorize]` page) | the same, behind the route guard | 31.64 KB¹ | **21.55 KB** |
| `RenderRoundTrip.RenderAndBuildPayload` | render + full payload | 35.36 KB | 35.36 KB |
| `LiveRenderRoundTrip.RenderOnce` | first live render | 65.23 KB | 65.23 KB |
| `LiveRenderRoundTrip.RenderTenTimes` | ten renders of one tree | 152.73 KB | 152.73 KB |
| `LiveRenderRoundTrip.RenderKeyedList100_ShuffledEachIteration` | keyed reorder | 72.73 KB | 72.73 KB |
| `LiveRenderRoundTrip.RenderDeep_50UserComponents` | 50 nested components | 62.66 KB | 62.66 KB |
| `BuilderSurface.Entry` | re-render of 50 chain-built rows | 19.79 KB | 19.79 KB |
| `HandlerRegistration.Register200` / `1000` / `2000` | first render registering N handlers | 32.84 / 145.18 / 334.1 KB | not re-run |
| `HandlerDispatch.SyncHandler` / `AsyncHandler` | a click reaching its handler | 248 B | 248 B |
| `FrameDiffer.*_ReusedScratch` (100 / 1000 / 5000 rows) | the diff alone, steady state | 0 B, bar `RawGuidePage` | not re-run |

¹ The benchmark did not exist at `efb13c941`; this is the branch that added it, before its route memo.
A render with no event on the same two pages (`RenderOnly`) is 17.17 KB and 17.37 KB, so the endpoint
adds about 1 KB to a click on an open page and 4 KB behind the guard.

`Rask.Benchmarks -- allocation-profile [rows|page]` names the types behind a number here.
`allocation-profile --check` is the gate CI runs: a live update of the 20-row page against
`allocation-budget.csv`. The budget is the count with tiered PGO off — `scripts/run-benchmarks-local.sh`
sets `DOTNET_TieredPGO=0` on that process — which reads 2,504 B on every run; with it on, the measured
window straddles the tier-up and the same binary reads anything from 2,397 to 2,514 B.

Session memory, from `session-footprint` on `efb13c941` (bytes retained per session):

| Page | Unconnected | Connected | Connected sessions per GiB |
|---|---:|---:|---:|
| Empty shell (415 B of HTML) | 11,832 | 23,772 | 45,168 |
| 5 rows | 24,871 | 46,438 | 23,122 |
| 200 rows | 504,711 | 856,415 | 1,253 |
| 1,000 rows | 2,803,382 | 4,244,136 | 252 |
