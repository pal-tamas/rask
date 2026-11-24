using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>Reads rask-strict.globalconfig — the file <c>&lt;RaskStrict&gt;</c> adds to an app's every project.</summary>
internal static class StrictConfig
{
    /// <summary>The severity the file gives <paramref name="id" />.</summary>
    public static ReportDiagnostic Severity(string id)
    {
        var line = File.ReadLines(RepoPath("src", "Rask.Core", "build", "rask-strict.globalconfig"))
            .Single(l => l.StartsWith("dotnet_diagnostic." + id + ".severity", StringComparison.Ordinal));

        return line.Split('=')[1].Trim() switch
        {
            "error" => ReportDiagnostic.Error,
            "warning" => ReportDiagnostic.Warn,
            "suggestion" => ReportDiagnostic.Info,
            _ => ReportDiagnostic.Suppress,
        };
    }

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory!.FullName, .. parts]);
    }
}
