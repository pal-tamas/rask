using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     Does Rask.Pdf's pinned browser start inside the image a template's Dockerfile builds?
/// </summary>
/// <remarks>
///     Each template's runtime stage is built as written, its build stage swapped for one that hands over the pinned
///     headless shell, for the Docker daemon's own architecture. Then the shell must link against nothing missing and
///     print a page. The x64 shell needs <c>libatk-bridge-2.0.so.0</c> and the arm64 one does not, so the image went
///     out without it and printed nothing in production, where it was the first to say so.
/// </remarks>
public sealed partial class TemplateImagePdfShellE2ETests
{
    private const string Shell = "/opt/rask/chrome/chrome-headless-shell";

    [Fact]
    public async Task Every_template_image_starts_the_pinned_pdf_shell()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);
        var platform = await DockerPlatform();
        var context = Path.Combine(Path.GetTempPath(), "rask-pdf-image", Guid.NewGuid().ToString("N")[..8]);
        await StageShell(platform, Path.Combine(context, "chrome-shell"));
        Directory.CreateDirectory(Path.Combine(context, "app"));
        File.WriteAllText(Path.Combine(context, "app", ".keep"), "");

        var failures = new List<string>();
        foreach (var template in TemplatesWithTheShell())
        {
            failures.AddRange(await Probe(template, context));
        }

        CliBuildE2E.TryDeleteDirectory(context);
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    private static async Task<IEnumerable<string>> Probe(string template, string context)
    {
        var tag = $"rask-pdf-image-{Path.GetFileName(Path.GetDirectoryName(template))}:probe";
        File.WriteAllText(Path.Combine(context, "Dockerfile"), WithStagedShell(File.ReadAllText(template)));
        var name = Path.GetRelativePath(CliBuildE2E.FindRepoRoot(), template);

        var (built, buildLog) = await DeployE2E.RunAsync("docker", ["build", "-t", tag, context]);
        if (built != 0)
        {
            return [$"{name}: the runtime stage did not build.\n{Tail(buildLog)}"];
        }

        var (_, linked) = await Docker(tag, $"ldd {Shell}");
        var (printed, output) = await Docker(tag,
            $"{Shell} --headless --disable-gpu --no-pdf-header-footer --print-to-pdf=/tmp/probe.pdf 'data:text/html,<h1>Rask</h1>' && head -c 5 /tmp/probe.pdf");
        await DeployE2E.RunAsync("docker", ["image", "rm", "-f", tag]);

        var failures = new List<string>();
        if (linked.Split('\n').Where(line => line.Contains("not found", StringComparison.Ordinal)).ToList() is { Count: > 0 } missing)
        {
            failures.Add($"{name}: the shell links against libraries the image does not install — add their packages to its apt-get line:\n{string.Join('\n', missing)}");
        }

        if (printed != 0 || !output.Contains("%PDF-", StringComparison.Ordinal))
        {
            failures.Add($"{name}: the shell did not print a page (exit {printed}).\n{Tail(output)}");
        }

        return failures;
    }

    /// <summary>The image's own user and flags as Rask.Pdf starts them: sandboxed, as in production.</summary>
    private static Task<(int Exit, string Output)> Docker(string tag, string script) =>
        DeployE2E.RunAsync("docker", ["run", "--rm", "--entrypoint", "sh", tag, "-c", script]);

    /// <summary>The template's runtime stage verbatim, after a build stage holding only the shell and an empty app.</summary>
    private static string WithStagedShell(string dockerfile)
    {
        var runtime = dockerfile[dockerfile.IndexOf("FROM mcr.microsoft.com/dotnet/aspnet", StringComparison.Ordinal)..];
        return "FROM scratch AS build\nCOPY app /app\nCOPY chrome-shell /chrome-shell\n\n" + Directive().Replace(runtime, "");
    }

    private static IEnumerable<string> TemplatesWithTheShell()
    {
        var templates = Directory.EnumerateFiles(Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates"), "Dockerfile", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("/opt/rask/chrome", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(templates);
        return templates;
    }

    private static async Task<string> DockerPlatform()
    {
        var (exit, arch) = await DeployE2E.RunAsync("docker", ["version", "--format", "{{.Server.Arch}}"]);
        var inCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        Assert.SkipWhen(exit != 0 && !inCi, $"No usable Docker daemon here, so no template image was built: {arch.Trim()}");
        Assert.True(exit == 0, $"CI has no usable Docker daemon for the template image check: {arch}");
        return arch.Trim() switch
        {
            "amd64" => "linux64",
            "arm64" => "linux-arm64",
            var other => throw new InvalidOperationException($"Rask.Pdf pins no headless shell for linux/{other}."),
        };
    }

    /// <summary>The pinned archive for <paramref name="platform"/>, checked against its pinned SHA-256 and unpacked.</summary>
    private static async Task StageShell(string platform, string target)
    {
        var pin = XDocument.Load(Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Pdf", "build", "Rask.Pdf.ChromePin.props"));
        string Pinned(string property) => pin.Descendants(property).Single().Value.Trim();
        var version = Pinned("RaskPdfChromeVersion");
        var sha256 = Pinned("RaskPdfChromeSha256_" + platform.Replace("linux-", "linux_", StringComparison.Ordinal));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var archive = await http.GetByteArrayAsync(
            new Uri($"https://storage.googleapis.com/chrome-for-testing-public/{version}/{platform}/chrome-headless-shell-{platform}.zip"));
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(archive)));

        var unpacked = Path.Combine(Path.GetDirectoryName(target)!, "..", Path.GetFileName(Path.GetDirectoryName(target)) + ".unpacked");
        await using (var zip = new MemoryStream(archive))
        {
            ZipFile.ExtractToDirectory(zip, unpacked);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(Directory.EnumerateDirectories(unpacked).Single(), target);
        Directory.Delete(unpacked);
    }

    private static string Tail(string text) => string.Join('\n', text.Split('\n').TakeLast(30));

    /// <summary>A template's conditional block, which <c>rask new</c> keeps or drops; dropped here.</summary>
    [GeneratedRegex(@"^# rask:if .*?^# rask:end\n", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Directive();
}
