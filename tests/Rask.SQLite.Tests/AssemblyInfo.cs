// SQLite connection pooling is process-global, and so is SqliteConnection.ClearAllPools(): calling it
// disposes the underlying sqlite3 handle of connections that are *currently leased and in use*, not just
// idle ones (verified against plain Microsoft.Data.Sqlite — Pooling=False immunises it). Several classes
// here open pooled connections and call ClearAllPools() in Dispose to release the temp-file handle. Run in
// parallel, one class's teardown can clear the pool out from under another class's in-flight writers — the
// concurrency stress test especially — surfacing a flaky ObjectDisposedException('SQLitePCL.sqlite3').
// Serialise the assembly so no teardown races another class's live connections. (The SQLite load harness
// serialises its arms for the same reason — see benchmarks/Rask.Benchmarks.Sqlite/Program.cs.)
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
