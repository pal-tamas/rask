using BenchmarkDotNet.Running;
using Rask.Benchmarks;

// `bundle-size` invokes the one-shot published-bundle size walker (PR1 baseline tool).
// Anything else passes straight through to BenchmarkDotNet.
if (args.Length >= 1 && args[0] == "bundle-size")
{
    return BundleSizeReport.Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
