namespace Rask.Server.Tests.Infrastructure;

// Test classes that build a host with an explicit environment, and so decide the value of the
// process-global LiveOptions.IsDevelopment for as long as that host lives.
//
// UseRask claims it with `??=` — first host in the process wins and it is never revised — so two hosts
// disagreeing about their environment is not a thing the product has to handle, but it IS a thing a test
// run produces. RaskTestHost.Dispose now clears it, which makes each host claim it fresh; running these
// classes in parallel would still let one host's answer land while another's dev-gated assertion reads
// it. Serialise them.
//
// Add this collection's [Collection] attribute to any new test class that passes `environment:` to
// RaskTestHost.Create.
[CollectionDefinition("HostEnvironment", DisableParallelization = true)]
public class HostEnvironmentCollection
{
}
