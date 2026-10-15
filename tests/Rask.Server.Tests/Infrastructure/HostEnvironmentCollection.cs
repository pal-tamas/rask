namespace Rask.Server.Tests.Infrastructure;

// Test classes that build a host with an explicit environment. Each host answers for itself now (HostMode, in
// its own services), so they no longer race a process-wide flag; they stay serial because several also read the
// process-wide diagnostics sink and the scoped-CSS minify switch, which a host still claims with `??=`.
//
// Add this collection's [Collection] attribute to any new test class that passes `environment:` to
// RaskTestHost.Create.
[CollectionDefinition("HostEnvironment", DisableParallelization = true)]
public class HostEnvironmentCollection
{
}
