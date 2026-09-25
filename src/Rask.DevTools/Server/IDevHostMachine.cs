namespace Rask.DevTools.Server;

/// <summary>The machine facts <see cref="EditorDevHost.Resolve" /> reads.</summary>
internal interface IDevHostMachine
{
    /// <summary>Where <c>rask dev</c> keeps its state: <c>~/.rask</c>.</summary>
    string Root { get; }

    /// <summary>The port the dev host listens on here.</summary>
    int HttpsPort { get; }

    /// <summary>Linux's <c>ip_unprivileged_port_start</c>, or null where there is no such limit to read.</summary>
    int? UnprivilegedPortStart { get; }

    bool FileExists(string path);

    /// <summary>The hosts file's text, or null when it cannot be read.</summary>
    string? ReadHosts();
}
