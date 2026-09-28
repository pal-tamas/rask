using System.ComponentModel;

namespace Rask.Cli;

/// <summary>How far <c>rask deploy</c> is allowed to go in changing the host.</summary>
internal enum SetupMode
{
    /// <summary>Ask first on a terminal; refuse to touch the host when there's nobody to ask.</summary>
    Ask,

    /// <summary>Do it without asking (<c>--setup-host</c>) — the CI/scripted path.</summary>
    Forced,

    /// <summary>Never touch the host (<c>--no-setup-host</c>); fail with guidance instead.</summary>
    Disabled,
}
