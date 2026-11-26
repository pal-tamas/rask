namespace Rask.Cli.Commands;

/// <summary>
/// The environment a deploy sets on an app container by default, added as <c>-e KEY=VALUE</c> only when the user's
/// own environment does not name the same key. Docker gives an inline <c>-e</c> precedence over <c>--env-file</c>,
/// so emitting a default the user also set would silently override the user's value whenever theirs travels in the
/// env file.
/// </summary>
internal sealed class RunDefaults
{
    private readonly List<string> _args;
    private readonly HashSet<string> _userKeys;

    /// <summary>Collect the keys the user set, spelled as .NET configuration reads them.</summary>
    /// <param name="args">The <c>docker run</c> argument list the defaults are appended to.</param>
    /// <param name="env">The user's <c>KEY=VALUE</c> entries — from <c>--env</c> and their own <c>--env-file</c> alike.</param>
    internal RunDefaults(List<string> args, IReadOnlyList<string> env)
    {
        _args = args;
        _userKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in DeployEnvironment.EnvKeysOf(env))
        {
            _userKeys.Add(ConfigurationKey(key));
        }
    }

    /// <summary>Add <c>-e <paramref name="key"/>=<paramref name="value"/></c> unless the user set that key.</summary>
    internal void Add(string key, string value)
    {
        if (!_userKeys.Contains(ConfigurationKey(key)))
        {
            _args.AddRange(["-e", $"{key}={value}"]);
        }
    }

    /// <summary>
    /// The key as .NET configuration sees it: the environment-variable provider reads <c>__</c> as the <c>:</c>
    /// separator, and the set it is looked up in compares case-insensitively, as configuration keys do.
    /// </summary>
    private static string ConfigurationKey(string key) => key.Replace("__", ":", StringComparison.Ordinal);
}
