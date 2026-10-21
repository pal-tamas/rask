using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Background;

/// <summary>What counts as "the same work" for <c>.UnlessWaiting()</c>, as the value of <see cref="Job.WaitingKey" />.</summary>
/// <remarks>
///     The same lane, the same tenant, the same registered job kind and the byte-identical serialized payload. Nothing
///     else goes in: not when it was enqueued, not when it is due, not who enqueued it, not the row's id. A job whose
///     own properties carry a clock or an id is only the same as one carrying the same values.
/// </remarks>
internal static class JobWaitingKey
{
    /// <summary>The length of a key: a SHA-256, as lowercase hex.</summary>
    internal const int Length = 64;

    internal static string Of(string type, string payload, string? lane, Guid? tenant)
    {
        // Each part but the last is prefixed with its length, so no lane or type can run into the next part and
        // make two different jobs spell one key.
        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{lane?.Length ?? -1}:{lane}|{tenant?.ToString("N") ?? "-"}|{type.Length}:{type}|{payload}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
