using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Testing;

/// <summary>
///     One file staged in a <see cref="TestFileBackend" />. It <em>is</em> the <see cref="IRaskFile" /> the
///     handler receives, so a test can compare identity as well as content.
/// </summary>
public sealed class TestFile : IRaskFile
{
    internal TestFile(string reference, string name, byte[] bytes, string contentType,
        DateTimeOffset lastModified)
    {
        Ref = reference;
        Name = name;
        Bytes = bytes;
        ContentType = contentType;
        LastModified = lastModified;
    }

    /// <summary>The handle the event payload refers to this file by.</summary>
    public string Ref { get; }

    /// <summary>The staged content.</summary>
    public byte[] Bytes { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public long Size => Bytes.Length;

    /// <inheritdoc />
    public string ContentType { get; }

    /// <inheritdoc />
    public DateTimeOffset LastModified { get; }

    /// <summary>This file's entry in an event payload — the same metadata a real client would send.</summary>
    public string Metadata =>
        "{\"ref\":" + JsonSerializer.Serialize(Ref)
                    + ",\"name\":" + JsonSerializer.Serialize(Name)
                    + ",\"size\":" + Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"type\":" + JsonSerializer.Serialize(ContentType)
                    + ",\"lastModified\":"
                    + LastModified.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "}";

    /// <inheritdoc />
    public Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        // Enforced here as the real backends do, so a test catches a component that forgot to raise the
        // limit for a large upload instead of only finding out on a real file.
        if (Size > maxAllowedSize)
        {
            throw new IOException($"File '{Name}' is {Size} bytes, exceeds maxAllowedSize of {maxAllowedSize}.");
        }

        return new MemoryStream(Bytes, writable: false);
    }
}
