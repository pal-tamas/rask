using BenchmarkDotNet.Running;
using Rask.Benchmarks.VsBlazor.Reports;

// Mirror Rask.Benchmarks/Program.cs: subcommands first, then default to BDN.
//   payload-bytes   — deterministic wire-bytes-per-update report (no measurement noise), CSV
//   keyed-list-dump — per-op diff breakdown for the keyed-reorder scenarios
//   mem-footprint   — deterministic retained-managed-heap-per-tree report (no BDN noise), CSV
// Anything else passes straight through to BenchmarkDotNet.
if (args.Length >= 1 && args[0] == "payload-bytes")
{
    return VsBlazorPayloadBytesReport.Run(args);
}

if (args.Length >= 1 && args[0] == "keyed-list-dump")
{
    return KeyedListDiffDump.Run(args);
}

if (args.Length >= 1 && args[0] == "mem-footprint")
{
    return VsBlazorMemFootprintReport.Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
