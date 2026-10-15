namespace Rask.Cli;

/// <summary>
/// A DNS provider the shared proxy can prove a domain with (ACME DNS-01), which is the only way Let's Encrypt
/// issues a wildcard certificate (<c>*.example.com</c>). <c>--dns azure</c> is the one there is: Caddy with the
/// <c>caddy-dns/azure</c> module, writing the challenge's TXT record into an Azure DNS zone with a service
/// principal's credentials.
///
/// <para>The credentials live in the proxy container's environment, never in a file Rask writes: they come from
/// <c>--dns-env-file</c> on the deploy that (re)creates the proxy, and the Caddyfile names them as
/// <c>{env.…}</c> placeholders, resolved inside Caddy at the moment it talks to Azure — so they are not part of
/// the config the admin API holds, and not in <c>.rask/deploy.json</c> or any label.</para>
/// </summary>
internal sealed record DnsProvider(string Name, string Image, string Module, IReadOnlyList<(string Field, string Variable)> Fields)
{
    /// <summary>The label on the proxy container naming the provider it was built with.</summary>
    public const string Label = "rask.dns";

    public static readonly DnsProvider Azure = new(
        "azure",
        "rask-caddy:dns-azure",
        "github.com/caddy-dns/azure",
        [
            ("tenant_id", "AZURE_TENANT_ID"),
            ("client_id", "AZURE_CLIENT_ID"),
            ("client_secret", "AZURE_CLIENT_SECRET"),
            ("subscription_id", "AZURE_SUBSCRIPTION_ID"),
            ("resource_group_name", "AZURE_RESOURCE_GROUP_NAME"),
        ]);

    public static IReadOnlyList<DnsProvider> All { get; } = [Azure];

    /// <summary>The provider called <paramref name="name"/> (as <c>--dns</c> and the proxy's label spell it), or <c>null</c>.</summary>
    public static DnsProvider? Named(string? name) =>
        All.FirstOrDefault(provider => string.Equals(provider.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The environment variables the credentials file has to set.</summary>
    public IEnumerable<string> Variables => Fields.Select(entry => entry.Variable);

    /// <summary>
    /// The image's Dockerfile: Caddy rebuilt with the provider's module by the official builder image, then copied
    /// over the stock binary — everything else (config paths, the data volume's layout) stays exactly as caddy:2 has it.
    /// </summary>
    public string Dockerfile =>
        "FROM caddy:2-builder AS build\n"
        + $"RUN xcaddy build --with {Module}\n"
        + "FROM caddy:2\n"
        + "COPY --from=build /usr/bin/caddy /usr/bin/caddy\n";

    /// <summary>The <c>tls</c> block of a site whose certificate is proven over DNS (one tab in, as a site's directives are).</summary>
    public string TlsBlock()
    {
        var lines = Fields.Select(entry => $"\t\t\t{entry.Field} {{env.{entry.Variable}}}\n");
        return $"\ttls {{\n\t\tdns {Name} {{\n{string.Concat(lines)}\t\t}}\n\t}}\n";
    }
}
