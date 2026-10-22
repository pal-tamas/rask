using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     Does Rask.Pdf's pinned browser start inside the image a template's Dockerfile builds, on a host like production?
/// </summary>
/// <remarks>
///     Each template's runtime stage is built as written, its build stage swapped for one that hands over the pinned
///     headless shell, for the Docker daemon's own architecture. Then the shell must link against nothing missing and
///     print a page, started as the image starts it, on a host that refuses a container user namespaces (Ubuntu 24.04
///     and later do, through AppArmor; a seccomp profile stands in for that, so every runner is such a host). Started
///     sandboxed there, it must fail with the cause Rask.Pdf names. The x64 shell needs <c>libatk-bridge-2.0.so.0</c>
///     and the arm64 one does not, so the image went out without it and printed nothing in production.
/// </remarks>
public sealed partial class TemplateImagePdfShellE2ETests
{
    private const string Shell = "/opt/rask/chrome/chrome-headless-shell";

    // Rask.Pdf's own flags, less the DevTools pipe it drives the browser over.
    private const string Flags = "--headless --disable-gpu --disable-dev-shm-usage --no-first-run --disable-extensions --hide-scrollbars";

    // A host that refuses unprivileged user namespaces: unshare and clone(CLONE_NEWUSER) fail, clone3 falls back to clone.
    private const string RestrictedHost = """
        {
          "defaultAction": "SCMP_ACT_ALLOW",
          "syscalls": [
            { "names": ["unshare"], "action": "SCMP_ACT_ERRNO", "errnoRet": 1 },
            { "names": ["clone3"], "action": "SCMP_ACT_ERRNO", "errnoRet": 38 },
            { "names": ["clone"], "action": "SCMP_ACT_ERRNO", "errnoRet": 1,
              "args": [{ "index": 0, "value": 268435456, "valueTwo": 268435456, "op": "SCMP_CMP_MASKED_EQ" }] }
          ]
        }
        """;

    [Fact]
    public async Task Every_template_image_starts_the_pinned_pdf_shell()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);
        var platform = await DockerPlatform();
        var context = Path.Combine(Path.GetTempPath(), "rask-pdf-image", Guid.NewGuid().ToString("N")[..8]);
        await StageShell(platform, Path.Combine(context, "chrome-shell"));
        Directory.CreateDirectory(Path.Combine(context, "app"));
        File.WriteAllText(Path.Combine(context, "app", ".keep"), "");
        var seccomp = await RestrictedHostProfile();

        var failures = new List<string>();
        foreach (var template in TemplatesWithTheShell())
        {
            failures.AddRange(await Probe(template, context, seccomp));
        }

        CliBuildE2E.TryDeleteDirectory(context);
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    private static async Task<IEnumerable<string>> Probe(string template, string context, RestrictedProfile seccomp)
    {
        var tag = $"rask-pdf-image-{Path.GetFileName(Path.GetDirectoryName(template))}:probe";
        File.WriteAllText(Path.Combine(context, "Dockerfile"), WithStagedShell(File.ReadAllText(template)));
        var name = Path.GetRelativePath(CliBuildE2E.FindRepoRoot(), template);

        var (built, buildLog) = await DeployE2E.RunAsync("docker", ["build", "-t", tag, context]);
        if (built != 0)
        {
            return [$"{name}: the runtime stage did not build.\n{Tail(buildLog)}"];
        }

        var (_, linked) = await Docker(tag, seccomp, $"ldd {Shell}");
        var (printed, output) = await Docker(tag, seccomp,
            $"{Shell} $([ \"$Rask__Pdf__NoSandbox\" = true ] && echo --no-sandbox) {Flags} --print-to-pdf=/tmp/probe.pdf 'data:text/html,<h1>Rask</h1>' && head -c 5 /tmp/probe.pdf");
        var (_, sandboxed) = await Docker(tag, seccomp, $"{Shell} {Flags} --dump-dom about:blank");
        await DeployE2E.RunAsync("docker", ["image", "rm", "-f", tag]);

        var failures = new List<string>();
        if (linked.Split('\n').Where(line => line.Contains("not found", StringComparison.Ordinal)).ToList() is { Count: > 0 } missing)
        {
            failures.Add($"{name}: the shell links against libraries the image does not install — add their packages to its apt-get line:\n{string.Join('\n', missing)}");
        }

        if (printed != 0 || !output.Contains("%PDF-", StringComparison.Ordinal))
        {
            failures.Add($"{name}: the shell did not print a page as the image starts it (exit {printed}).\n{Tail(output)}");
        }

        if (ChromiumStartFailure.Explain(sandboxed.Split('\n')) is not { } cause
            || !cause.Contains("Rask:Pdf:NoSandbox=true", StringComparison.Ordinal))
        {
            failures.Add($"{name}: started sandboxed on a host that refuses user namespaces, the shell did not fail with the cause Rask.Pdf names.\n{Tail(sandboxed)}");
        }

        return failures;
    }

    /// <summary>A script in the image, as its own user with its own environment, on the restricted host.</summary>
    private static Task<(int Exit, string Output)> Docker(string tag, RestrictedProfile seccomp, string script) =>
        DeployE2E.RunAsync(seccomp.Cli, ["run", "--rm", "--security-opt", $"seccomp={seccomp.Path}", "--entrypoint", "sh", tag, "-c", script]);

    /// <summary>
    ///     The restricted host's profile on disk. Docker's CLI sends a profile's contents and Podman's API wants a path,
    ///     so under Podman the profile goes through its own CLI, from the home folder its machine VM can see.
    /// </summary>
    private static async Task<RestrictedProfile> RestrictedHostProfile()
    {
        var (_, engine) = await DeployE2E.RunAsync("docker", ["version", "--format", "{{range .Server.Components}}{{.Name}} {{end}}"]);
        var podman = engine.Contains("Podman", StringComparison.Ordinal);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "rask-pdf-image");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "restricted-host.seccomp.json");
        File.WriteAllText(path, RestrictedHost);
        return new RestrictedProfile(podman ? "podman" : "docker", path);
    }

    private sealed record RestrictedProfile(string Cli, string Path);

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
