using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.Core.Dom.Build;

public sealed class RaskMdnRefresh : Task
{
    // An absolute path, so a PATH entry cannot stand in for bash. /bin is the norm; NixOS has neither.
    private static readonly string BashPath =
        Array.Find(new[] { "/bin/bash", "/usr/bin/bash", "/run/current-system/sw/bin/bash" }, File.Exists)
        ?? "/bin/bash";

    [Required] public string Snapshot { get; set; } = "";

    [Required] public string RefreshScript { get; set; } = "";

    // ~/.rask/mdn: where the once-a-day stamp lives.
    [Required] public string StampDirectory { get; set; } = "";

    public override bool Execute()
    {
        // Every failure here is a message, never an error: an unreachable MDN falls back to the committed
        // snapshot, and the build goes on.
        try
        {
            Directory.CreateDirectory(StampDirectory);
            var stamp = Path.Combine(StampDirectory, "last-check");
            if (File.Exists(stamp) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromDays(1))
            {
                return true;
            }

            var current = DomEmitter.Sources(File.ReadAllText(Snapshot));
            var latest = MdnLatest.Resolve();
            File.WriteAllText(stamp, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            var newer = latest.Where(kv => !current.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal)).ToList();
            if (newer.Count == 0)
            {
                return true;
            }

            Log.LogMessage(MessageImportance.High, "MDN has newer data ({0}); refreshing the element snapshot…",
                string.Join(", ", newer.Select(kv => kv.Key + " " + kv.Value)));
            if (!Run(latest))
            {
                return true;
            }

            Log.LogMessage(MessageImportance.High, "MDN refreshed: commit {0}", Snapshot);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or AggregateException or System.Threading.Tasks.TaskCanceledException
                                      or FormatException or InvalidOperationException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            Log.LogMessage(MessageImportance.High, "MDN refresh skipped ({0}); building from the committed snapshot.", e.Message);
        }

        return true;
    }

    private bool Run(IReadOnlyDictionary<string, string> pins)
    {
        var info = new ProcessStartInfo(BashPath, "\"" + RefreshScript + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["RASK_MDN_BCD"] = pins["@mdn/browser-compat-data"];
        info.Environment["RASK_MDN_IDL"] = pins["@webref/idl"];
        info.Environment["RASK_MDN_ELEMENTS"] = pins["@webref/elements"];
        info.Environment["RASK_MDN_EVENTS"] = pins["@webref/events"];
        info.Environment["RASK_MDN_WEBREF"] = pins["webref/dfns"];
        info.Environment["RASK_MDN_WEBIDL2"] = pins["webidl2"];
        info.Environment["RASK_MDN_PARSE5"] = pins["parse5"];
        info.Environment["RASK_MDN_ARIA"] = pins["w3c/aria"];
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill();
            Log.LogMessage(MessageImportance.High, "MDN refresh timed out; building from the committed snapshot.");
            return false;
        }

        if (process.ExitCode != 0)
        {
            Log.LogMessage(MessageImportance.High, "MDN refresh failed; building from the committed snapshot.\n{0}", error.Result);
            return false;
        }

        Log.LogMessage(MessageImportance.Normal, output.Result);
        return true;
    }
}
