namespace Rask.Storage.Backends;

/// <summary>
/// An Azure storage account, read from its connection string. A class, not a record, and its <c>ToString</c>
/// names the account only: a generated one would print the key.
/// </summary>
internal sealed class AzureAccount
{
    internal const string DevelopmentAccountName = "devstoreaccount1";

    // Azurite's well-known development key — public, documented, and useless outside the emulator.
    internal const string DevelopmentAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private const string Setting = "Storage__Azure__ConnectionString";

    private AzureAccount(string blobEndpoint, string accountName, byte[]? accountKey, string? sharedAccessSignature)
    {
        BlobEndpoint = blobEndpoint;
        AccountName = accountName;
        AccountKey = accountKey;
        SharedAccessSignature = sharedAccessSignature;
    }

    /// <summary>The blob service endpoint, with no trailing slash.</summary>
    public string BlobEndpoint { get; }

    public string AccountName { get; }

    public byte[]? AccountKey { get; }

    /// <summary>A SAS token without its leading <c>?</c>, when the account is reached with one instead of a key.</summary>
    public string? SharedAccessSignature { get; }

    /// <summary>Whether requests are signed with the account key — and so whether temporary URLs can be signed by Azure.</summary>
    public bool CanSign => AccountKey is not null;

    public override string ToString() => $"Azure storage account {AccountName} at {BlobEndpoint}";

    /// <summary>Parses a connection string. Every error names the part that is wrong and never repeats a value.</summary>
    internal static AzureAccount Parse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{Setting} is required when Storage__Provider is Azure: "
                + "DefaultEndpointsProtocol=https;AccountName=…;AccountKey=…;EndpointSuffix=core.windows.net, or "
                + "BlobEndpoint=…;SharedAccessSignature=…, or UseDevelopmentStorage=true for Azurite.");
        }

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // The first '=' only: keys and SAS tokens contain '=' themselves.
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                throw new InvalidOperationException($"{Setting} has a part that is not name=value.");
            }

            parts[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }

        if (parts.TryGetValue("UseDevelopmentStorage", out var development)
            && bool.TryParse(development, out var isDevelopment) && isDevelopment)
        {
            return new AzureAccount("http://127.0.0.1:10000/" + DevelopmentAccountName, DevelopmentAccountName,
                Convert.FromBase64String(DevelopmentAccountKey), null);
        }

        parts.TryGetValue("AccountName", out var name);

        byte[]? key = null;
        if (parts.TryGetValue("AccountKey", out var keyText) && keyText.Length > 0)
        {
            var buffer = new byte[(keyText.Length * 3 / 4) + 3];
            if (!Convert.TryFromBase64String(keyText, buffer, out var written))
            {
                throw new InvalidOperationException($"The AccountKey in {Setting} is not base64.");
            }

            key = buffer[..written];
        }

        var sas = parts.TryGetValue("SharedAccessSignature", out var sasText) && sasText.TrimStart('?') is { Length: > 0 } token
            ? token
            : null;

        string endpoint;
        if (parts.TryGetValue("BlobEndpoint", out var blobEndpoint))
        {
            if (!Uri.TryCreate(blobEndpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            {
                throw new InvalidOperationException($"The BlobEndpoint in {Setting} is not an absolute http(s) URL.");
            }

            endpoint = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
        else
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"{Setting} needs an AccountName (or a BlobEndpoint).");
            }

            var protocol = parts.GetValueOrDefault("DefaultEndpointsProtocol", "https");
            if (protocol is not ("https" or "http"))
            {
                throw new InvalidOperationException($"The DefaultEndpointsProtocol in {Setting} must be https or http.");
            }

            endpoint = $"{protocol}://{name}.blob.{parts.GetValueOrDefault("EndpointSuffix", "core.windows.net")}";
        }

        if (key is null && sas is null)
        {
            throw new InvalidOperationException($"{Setting} needs an AccountKey or a SharedAccessSignature.");
        }

        if (key is not null && string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{Setting} has an AccountKey but no AccountName to sign with.");
        }

        return new AzureAccount(endpoint, name ?? new Uri(endpoint).Host.Split('.')[0], key, sas);
    }
}
