// Two of the things UseRask configures are PROCESS-WIDE statics, not per-server state:
// ScopedAssetBundle.BakedDirectory and LiveOptions.PathBase. Three classes here stand servers up
// (AssetEndpointParityTests, PathBaseEndpointTests, UseRaskTests), xUnit runs classes in parallel, and
// a second host therefore takes the first one's state away from it — disposal is worse still, since it
// resets the statics and a host that merely finishes can break one that is still serving.
//
// That is what made the local gate fail with
//   AssetEndpointParityTests.RegistryMiss_BakedBundleFile_NegotiatesPrecompressedSibling
//   Assert.Equal() Failure: Expected: OK, Actual: NotFound
// on a diff that touched none of it (#789). ProcessWideHostStateTests demonstrates the overlap
// directly rather than leaving this comment to be believed.
//
// The statics are not the bug: a real deployment has one host per process, which is why UseRask can set
// them at all. Only a test process holds two, so the fix belongs here. Matches the other suites that
// share process-wide state (Rask.Dashboard.Tests, Rask.SQLite.Tests).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
