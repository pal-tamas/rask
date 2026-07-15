using BenchmarkDotNet.Running;
using Rask.Benchmarks;

// `bundle-size` invokes the one-shot published-bundle size walker (PR1 baseline tool).
// `payload-bytes` prints the headline wire-size metric for the perf pass (counter on
// large page, keyed list reorder, text node update). Anything else passes straight
// through to BenchmarkDotNet.
if (args.Length >= 1 && args[0] == "bundle-size")
{
    return BundleSizeReport.Run(args);
}

if (args.Length >= 1 && args[0] == "payload-bytes")
{
    return PayloadBytesReport.Run(args);
}

// `session-footprint` answers "how many live sessions fit in 1 GB" across a page-size sweep;
// `session-churn` soaks and churns sessions to prove that number holds under sustained load.
if (args.Length >= 1 && args[0] == "session-footprint")
{
    return SessionFootprintReport.Run(args);
}

if (args.Length >= 1 && args[0] == "session-churn")
{
    return SessionChurnReport.Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
