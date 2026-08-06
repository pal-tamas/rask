using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The remembered settings for <c>rask deploy</c>, persisted at <c>.rask/deploy.json</c> so a repeat
/// deploy is a bare <c>rask deploy</c>. Convenience only — the server's live container labels remain the
/// multi-app source of truth. <strong>Secrets are never stored</strong>: only the <c>--env-file</c> path
/// is remembered, not the values inside it.
/// </summary>
internal sealed class DeployConfig
{
    public string? Host { get; set; }

    public string? Domain { get; set; }

    public int? Port { get; set; }

    public string? Name { get; set; }

    public string? Project { get; set; }

    public string? EnvFile { get; set; }

    /// <summary>The HTTP path <c>rask deploy</c> probes to confirm readiness before switching traffic (default <c>/health</c>).</summary>
    public string? HealthPath { get; set; }

    /// <summary>When <c>true</c>, skip the HTTP health probe and gate only on the container running.</summary>
    public bool? HealthCheckDisabled { get; set; }

    /// <summary>
    /// The <b>names</b> of the environment variables this app was last deployed with — never their values.
    ///
    /// <para>Values deliberately aren't remembered: this file is committed. But forgetting that the
    /// variables <em>exist</em> is how a redeploy silently starts the app without its database password.
    /// Recording the keys lets the next deploy notice one is missing and refuse, instead of shipping a
    /// half-configured app that starts, answers its health check, and is quietly broken.</para>
    /// </summary>
    public string[]? EnvKeys { get; set; }

    /// <summary>
    /// The port the app listens on <em>inside</em> its container — what the proxy is pointed at and what the
    /// health probe hits (default <c>8080</c>, which every Dockerfile <c>rask new --docker</c> emits uses).
    /// Only needed for a hand-written Dockerfile that listens elsewhere.
    /// </summary>
    public int? ContainerPort { get; set; }

    /// <summary>The <c>.rask/deploy.json</c> path under <paramref name="workingDirectory"/>.</summary>
    public static string PathFor(string workingDirectory) =>
        Path.Combine(workingDirectory, ".rask", "deploy.json");

    /// <summary>
    ///     Load the persisted config, or an empty one when the file is absent or unreadable. Pass
    ///     <paramref name="console" /> so an unreadable file is reported rather than silently ignored.
    /// </summary>
    public static DeployConfig Load(IFileSystem fileSystem, string workingDirectory, IConsole? console = null)
    {
        var path = PathFor(workingDirectory);
        if (!fileSystem.FileExists(path))
        {
            return new DeployConfig();
        }

        try
        {
            return JsonSerializer.Deserialize(fileSystem.ReadAllText(path), DeployConfigJsonContext.Default.DeployConfig)
                ?? new DeployConfig();
        }
        catch (JsonException ex)
        {
            // A hand-edited or corrupt file shouldn't wedge a deploy — fall back to flags/defaults. But
            // it must not do so in silence: a typo'd config looked exactly like no config at all, and
            // the remembered host quietly vanished with nothing said (#599).
            console?.WriteErrorLine(
                $"Ignoring {path}: it isn't valid JSON ({ex.Message.TrimEnd('.')}). Falling back to "
                + "flags and defaults — fix or delete the file to use it again.",
                ConsoleStyle.Warning);
            return new DeployConfig();
        }
    }

    /// <summary>
    ///     The reason this config can't be read, or <c>null</c> when there is nothing wrong with it.
    ///     What <c>rask doctor</c> reports; <see cref="Load" /> uses the same check to warn in passing.
    /// </summary>
    public static string? DescribeProblem(IFileSystem fileSystem, string workingDirectory)
    {
        var path = PathFor(workingDirectory);
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            JsonSerializer.Deserialize(fileSystem.ReadAllText(path), DeployConfigJsonContext.Default.DeployConfig);
            return null;
        }
        catch (JsonException ex)
        {
            return $"{path} isn't valid JSON: {ex.Message.TrimEnd('.')}.";
        }
    }

    /// <summary>Write the config to <c>.rask/deploy.json</c> (creating the <c>.rask</c> directory).</summary>
    public void Save(IFileSystem fileSystem, string workingDirectory)
    {
        var path = PathFor(workingDirectory);
        fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        fileSystem.WriteAllText(path, JsonSerializer.Serialize(this, DeployConfigJsonContext.Default.DeployConfig));
    }
}

/// <summary>Source-generated (reflection-free) serialization for <see cref="DeployConfig"/>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeployConfig))]
internal sealed partial class DeployConfigJsonContext : JsonSerializerContext;
