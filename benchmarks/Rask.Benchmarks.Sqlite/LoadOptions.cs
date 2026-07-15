using System.Globalization;

namespace Rask.Benchmarks.Sqlite;

/// <summary>Parsed command line. Defaults are tuned for a useful ad-hoc run without any arguments.</summary>
internal sealed record LoadOptions
{
    internal IReadOnlyList<int> Vus { get; init; } = [1, 4, 8, 16, 32, 64, 128, 256];

    internal TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(15);

    internal TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(5);

    internal TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Readers sweep against this many concurrent writers in the read-under-write workload.</summary>
    internal int Writers { get; init; } = 1;

    internal string? OutPath { get; init; }

    /// <summary>Restricts the gate to the hardware-independent Tier 1 invariants (see <see cref="LoadGate"/>).</summary>
    internal bool Ci { get; init; }

    internal static LoadOptions Parse(string[] args)
    {
        var options = new LoadOptions();

        for (var i = 1; i < args.Length; i++)
        {
            string Next(string name) => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"{name} needs a value.");

            switch (args[i])
            {
                case "--vus":
                    options = options with
                    {
                        Vus = Next("--vus")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
                            .ToArray(),
                    };
                    break;
                case "--duration":
                    options = options with { Duration = ParseDuration(Next("--duration")) };
                    break;
                case "--warmup":
                    options = options with { Warmup = ParseDuration(Next("--warmup")) };
                    break;
                case "--window":
                    options = options with { Window = ParseDuration(Next("--window")) };
                    break;
                case "--writers":
                    options = options with { Writers = int.Parse(Next("--writers"), CultureInfo.InvariantCulture) };
                    break;
                case "--out":
                    options = options with { OutPath = Next("--out") };
                    break;
                case "--ci":
                    options = options with { Ci = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return options;
    }

    /// <summary>Accepts a bare seconds count (<c>30</c>) or a suffixed span (<c>90s</c>, <c>10m</c>).</summary>
    private static TimeSpan ParseDuration(string value) => value switch
    {
        _ when value.EndsWith('m') =>
            TimeSpan.FromMinutes(double.Parse(value[..^1], CultureInfo.InvariantCulture)),
        _ when value.EndsWith('s') =>
            TimeSpan.FromSeconds(double.Parse(value[..^1], CultureInfo.InvariantCulture)),
        _ => TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture)),
    };

    private LoadOptions()
    {
    }
}
