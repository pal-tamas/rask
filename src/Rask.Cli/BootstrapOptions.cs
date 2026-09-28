namespace Rask.Cli;

/// <summary>What the user asked <c>rask deploy</c> to do to the host (after flags and defaults are merged).</summary>
/// <param name="DeployUser">The non-root login to create and switch to, or <c>null</c> to keep the current one.</param>
/// <param name="PublishedPort">The <c>--port</c> to open, or <c>null</c> in domain mode (which opens 80/443 for Caddy).</param>
/// <param name="ConnectPort">
/// The SSH port this session is actually connected on, resolved locally by <c>ssh -G</c>. Always
/// allowed through the firewall on top of whatever <c>sshd -T</c> reports — those differ when sshd is
/// socket-activated, and the port we're using is the one we cannot afford to close.
/// </param>
/// <param name="ContainerPort">
/// The port <em>inside</em> the app container that <see cref="PublishedPort"/> maps to. Needed on top
/// of the published port because Docker's DNAT happens in <c>nat/PREROUTING</c>, <em>before</em> the
/// filter rules run: by the time a packet reaches <c>DOCKER-USER</c> its destination port is already
/// the container's, so that — not the host's — is the number the firewall has to allow.
/// </param>
internal sealed record BootstrapOptions(string? DeployUser, bool Firewall, bool HardenSsh, int? PublishedPort, int? ConnectPort = null, int? ContainerPort = null)
{
    /// <summary>The default non-root login <c>rask deploy</c> creates on a box it's handed as root.</summary>
    public const string DefaultDeployUser = "deploy";
}
