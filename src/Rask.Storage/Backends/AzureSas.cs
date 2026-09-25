using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Storage.Backends;

/// <summary>
/// A read-only service SAS for one blob, signed with the account key — what makes an Azure temporary URL a
/// download that never passes through the app. <c>st</c> is omitted, so a clock slightly behind Azure's cannot
/// produce a URL that is not valid yet.
/// </summary>
internal static class AzureSas
{
    internal static string BlobRead(AzureAccount account, string container, string blobName, DateTimeOffset expiry,
        string contentType, string contentDisposition)
    {
        var expires = expiry.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var protocol = account.BlobEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "https,http";
        var stringToSign = StringToSign(account.AccountName, container, blobName, expires, protocol, contentType, contentDisposition);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(account.AccountKey!, Encoding.UTF8.GetBytes(stringToSign)));

        return string.Join('&',
            Pair("sv", AzureSharedKey.Version),
            Pair("spr", protocol),
            Pair("se", expires),
            Pair("sr", "b"),
            Pair("sp", "r"),
            Pair("rscd", contentDisposition),
            Pair("rsct", contentType),
            Pair("sig", signature));
    }

    /// <summary>The sixteen-field service SAS string-to-sign for versions 2020-12-06 and later.</summary>
    internal static string StringToSign(string accountName, string container, string blobName, string expires,
        string protocol, string contentType, string contentDisposition) =>
        string.Join('\n',
            "r",                                          // signedPermissions
            "",                                           // signedStart
            expires,                                      // signedExpiry
            $"/blob/{accountName}/{container}/{blobName}", // canonicalizedResource
            "",                                           // signedIdentifier
            "",                                           // signedIP
            protocol,                                     // signedProtocol
            AzureSharedKey.Version,                       // signedVersion
            "b",                                          // signedResource
            "",                                           // signedSnapshotTime
            "",                                           // signedEncryptionScope
            "",                                           // rscc
            contentDisposition,                           // rscd
            "",                                           // rsce
            "",                                           // rscl
            contentType);                                 // rsct

    private static string Pair(string name, string value) => name + "=" + Uri.EscapeDataString(value);
}
