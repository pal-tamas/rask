namespace Rask.Storage.Backends;

/// <summary>An S3 access key. A class, not a record: a record's generated <c>ToString</c> would print the secret.</summary>
internal sealed class S3Credential(string accessKeyId, string secretAccessKey, string? sessionToken)
{
    public string AccessKeyId { get; } = accessKeyId;

    public string SecretAccessKey { get; } = secretAccessKey;

    public string? SessionToken { get; } = sessionToken;

    public override string ToString() => "S3 access key " + AccessKeyId;
}
