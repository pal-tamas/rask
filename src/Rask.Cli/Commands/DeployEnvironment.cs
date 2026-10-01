
namespace Rask.Cli.Commands;

/// <summary>An app's runtime <c>KEY=VALUE</c> environment: its keys, what an env file can carry, and hiding its values.</summary>
internal static class DeployEnvironment
{
    /// <summary>
    /// Remembered keys that aren't being supplied this time. Ordinal + sorted so the message is stable.
    /// </summary>
    internal static IReadOnlyList<string> MissingEnvKeys(IReadOnlyList<string>? remembered, IReadOnlyList<string> supplied)
    {
        if (remembered is null || remembered.Count == 0)
        {
            return [];
        }

        var have = EnvKeysOf(supplied).ToHashSet(StringComparer.Ordinal);
        return [.. remembered.Where(k => !have.Contains(k)).Order(StringComparer.Ordinal)];
    }

    /// <summary>The KEY halves of a set of KEY=VALUE entries, de-duplicated and sorted.</summary>
    internal static string[] EnvKeysOf(IReadOnlyList<string> env) =>
    [
        .. env
            .Select(e => e.IndexOf('=', StringComparison.Ordinal) is var i and >= 0 ? e[..i] : e)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// An env file is line-oriented, so a value containing a newline can't round-trip through one.
    /// Those entries keep going in as <c>-e</c> rather than being silently truncated.
    /// </summary>
    internal static bool CanGoInEnvFile(string entry) =>
        entry.IndexOf('=', StringComparison.Ordinal) > 0 && !entry.Contains('\n') && !entry.Contains('\r');

    /// <summary>Replace every non-trivial <c>--env</c> value found in <paramref name="text"/> with an ellipsis.</summary>
    internal static string MaskSecrets(string text, IReadOnlyList<string> env)
    {
        foreach (var entry in env)
        {
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            // Very short values ("1", "true", a port) are not secrets and masking them would shred the
            // log into ellipses, destroying the diagnostics this dump exists to provide.
            var value = entry[(eq + 1)..];
            if (value.Length >= 6)
            {
                text = text.Replace(value, "…", StringComparison.Ordinal);
            }
        }

        return text;
    }

    // Turn KEY=secret into KEY=… for display, so a dry-run preview never prints secret values.
    internal static string[] RedactEnv(IReadOnlyList<string> env)
    {
        var redacted = new string[env.Count];
        for (var i = 0; i < env.Count; i++)
        {
            var eq = env[i].IndexOf('=', StringComparison.Ordinal);
            redacted[i] = eq >= 0 ? $"{env[i][..eq]}=…" : env[i];
        }

        return redacted;
    }
}
