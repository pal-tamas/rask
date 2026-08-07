namespace Rask.Core.Tests.Lifecycle;

// Test classes that mutate the framework's process-global observability state: Console.Error /
// Console.Out via Console.SetError / SetOut, and RaskDiagnostics.Sink (plus the ReportedOnce dedup set
// behind ReportOnce). Running two of those in parallel produces a flake where one captures the other's
// output, or — worse and silently — loses the sink entirely: both save `previous`, the second saves the
// FIRST one's capturing delegate, and restoring in that order leaves Sink pointing at a list nobody
// reads. Group them under one collection so xUnit serialises them.
//
// Add this collection's [Collection] attribute to any new test class that calls Console.SetError /
// Console.SetOut, assigns RaskDiagnostics.Sink, or calls ResetReportOnceForTests.
//
// Note what this does NOT buy: xUnit serialises members of a collection against each other, not against
// the rest of the assembly. Other collections still run in parallel and their diagnostics still land in
// whatever sink is installed — so a test in here must assert on the events IT provoked rather than on
// the whole capture. See PersistentStateDiagnosticTests for the shape.
[CollectionDefinition("ConsoleRedirect", DisableParallelization = true)]
public class ConsoleRedirectCollection
{
}
