namespace Rask.Server.Tests.Infrastructure;

// Test classes that swap the process-global RaskDiagnostics.Sink.
//
// The swap is a save/restore of shared mutable state with no mutual exclusion:
//
//     var previous = RaskDiagnostics.Sink;
//     RaskDiagnostics.Sink = captured.Enqueue;
//     try { ... } finally { RaskDiagnostics.Sink = previous; }
//
// Run two of those in parallel and the second saves the FIRST one's capturing delegate as its
// `previous`, so restoring in that order leaves the sink pointing at a queue nobody reads — and the
// event the first test is waiting for is delivered to a stranger, or to nobody. It shows up as
// `Assert.Contains() Failure ... Collection: []`: EMPTY, not slow.
//
// It fails OPEN, which is why it is worth serialising rather than tolerating. A test asserting "the
// framework reported X" passes whenever the sink happens not to be stolen — and StaticPageAuditTests
// exists specifically to prove that under-detection gets reported, so the safety net for the riskiest
// part of static detection is the thing with the hole in it.
//
// Add [Collection("DiagnosticsSink")] to any new test class in this assembly that assigns
// RaskDiagnostics.Sink. Rask.Core.Tests has the same arrangement under "ConsoleRedirect", which also
// covers Console.SetOut/SetError; this is the server-side counterpart it never got.
//
// What this does NOT buy: xUnit serialises members of a collection against each other. A test in here
// should still assert on the events IT provoked rather than on the whole capture.
[CollectionDefinition("DiagnosticsSink", DisableParallelization = true)]
public sealed class DiagnosticsSinkCollection;
