# Allocation baseline — BenchmarkDotNet

Pinned on `efb13c941` (2026-10-06), Apple M4 Pro, .NET 10.0.10, `--job short`. Compare **Allocated**: it is
deterministic. The times were taken on a machine at a load average near 100 and are not a baseline for
anything — re-run on a quiet machine before quoting one.

```bash
dotnet run -c Release --project tests/Rask.Benchmarks -- --job short --buildTimeout 1800 \
  --filter '*LiveSessionSendBenchmarks*' '*LiveRenderRoundTripBenchmarks*' '*RenderRoundTripBenchmarks*' \
  '*PageRequestBenchmarks*' '*HostDocumentRenderBenchmarks*' '*HandlerDispatchBenchmarks*' \
  '*HandlerRegistrationBenchmarks*' '*FrameDifferBenchmarks*' '*BuilderSurfaceBenchmarks*'
```

| Benchmark | What it is | Allocated |
|---|---|---:|
| `LiveSessionSend.RenderAndSend` | one live update, 20-row page: render, diff, send | 8,720 B |
| `LiveSessionSend.SendOutOfBand` | a non-render frame through the same path | 0 B |
| `HostDocumentRender.RenderAndSendWithoutToasts` | one live update of a host document | 9.36 KB |
| `HostDocumentRender.RenderAndSendWithBuiltInToasts` | the same, with the toast region mounted | 9.43 KB |
| `PageRequest.GetPage` | a first GET, end to end | 66.83 KB |
| `RenderRoundTrip.RenderAndBuildPayload` | render + full payload | 35.36 KB |
| `LiveRenderRoundTrip.RenderOnce` | first live render | 65.23 KB |
| `LiveRenderRoundTrip.RenderTenTimes` | ten renders of one tree | 152.73 KB |
| `LiveRenderRoundTrip.RenderKeyedList100_ShuffledEachIteration` | keyed reorder | 72.73 KB |
| `LiveRenderRoundTrip.RenderDeep_50UserComponents` | 50 nested components | 62.66 KB |
| `BuilderSurface.Entry` | re-render of 50 chain-built rows | 19.79 KB |
| `HandlerRegistration.Register200` / `1000` / `2000` | first render registering N handlers | 32.84 / 145.18 / 334.1 KB |
| `HandlerDispatch.SyncHandler` / `AsyncHandler` | a click reaching its handler | 248 B |
| `HandlerDispatch.UnknownHandler` | a stale id refused | 0 B |
| `FrameDiffer.*_ReusedScratch` (100 / 1000 / 5000 rows) | the diff alone, steady state | 0 B, bar `RawGuidePage` |

Session memory, from `session-footprint` on the same commit (bytes retained per session):

| Page | Unconnected | Connected | Connected sessions per GiB |
|---|---:|---:|---:|
| Empty shell (415 B of HTML) | 11,832 | 23,772 | 45,168 |
| 5 rows | 24,871 | 46,438 | 23,122 |
| 200 rows | 504,711 | 856,415 | 1,253 |
| 1,000 rows | 2,803,382 | 4,244,136 | 252 |
