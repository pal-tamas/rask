// SQLite connection pooling and SqliteConnection.ClearAllPools() are process-global: clearing the pool
// disposes the underlying sqlite3 handle of connections that are currently leased and in use, not just
// idle ones. These classes open pooled connections (RaskSqliteOpenUnderLockTests deliberately holds a
// write lock) and call ClearAllPools() in Dispose, so running them in parallel lets one class's teardown
// clear the pool out from under another's live connection — a flaky ObjectDisposedException('SQLitePCL.sqlite3').
// Serialise the assembly so no teardown races another class's live connections. See the sibling note in
// Rask.SQLite.Tests/AssemblyInfo.cs.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
