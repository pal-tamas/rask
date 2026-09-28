using Rask.Storage.Backends;

namespace Rask.Storage;

/// <summary>
/// Settings for <see cref="StorageProvider.Azure"/>. Requests are signed in-process (Shared Key, service SAS);
/// no cloud SDK is involved.
/// </summary>
public sealed class AzureStorageOptions
{
    /// <summary>One Put Blob creates at most 5000 MiB.</summary>
    internal const long MaxSinglePutBytes = 5000L * 1024 * 1024;

    /// <summary>
    /// The storage account's connection string. With <c>AccountName</c> and <c>AccountKey</c>, temporary URLs are
    /// signed by Azure itself and downloads never pass through the app; with <c>BlobEndpoint</c> and
    /// <c>SharedAccessSignature</c> only, the app serves them. <c>UseDevelopmentStorage=true</c> targets Azurite.
    /// Configuration: <c>Rask__Storage__Azure__ConnectionString</c> — pass it as a secret.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>The container. Configuration: <c>Rask__Storage__Azure__Container</c>.</summary>
    public string Container { get; set; } = "";

    internal void Validate()
    {
        // Parsing is the validation: it names the part that is wrong and never repeats the value.
        _ = AzureAccount.Parse(ConnectionString);

        if (!IsContainerName(Container))
        {
            throw new InvalidOperationException(
                $"Rask__Storage__Azure__Container '{Container}' is not a container name: 3 to 63 lowercase letters, digits and "
                + "single hyphens, starting and ending with a letter or digit.");
        }
    }

    private static bool IsContainerName(string? name)
    {
        if (name is not { Length: >= 3 and <= 63 } || !char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || (c == '-' && name[i - 1] != '-')))
            {
                return false;
            }
        }

        return true;
    }
}
