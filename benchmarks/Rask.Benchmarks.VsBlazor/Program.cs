using BenchmarkDotNet.Running;

// Mirror Rask.Benchmarks/Program.cs: subcommands first, then default to BDN.
//   payload-bytes   — deterministic byte-count report (no measurement noise), writes CSV
// Anything else passes straight through to BenchmarkDotNet.
if (args.Length >= 1 && args[0] == "payload-bytes")
{
    return Rask.Benchmarks.VsBlazor.Reports.VsBlazorPayloadBytesReport.Run(args);
}

if (args.Length >= 1 && args[0] == "keyed-list-dump")
{
    return Rask.Benchmarks.VsBlazor.Reports.KeyedListDiffDump.Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
