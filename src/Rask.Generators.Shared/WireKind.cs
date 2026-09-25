namespace Rask.Generators.Shared;

/// <summary>How a type is encoded on the wire.</summary>
internal enum WireKind
{
    /// <summary>A value with a direct JSON representation — number, string, bool.</summary>
    Scalar,

    /// <summary>An enum, encoded as its underlying numeric value so a rename does not break the wire.</summary>
    Enum,

    /// <summary>A nullable value or reference type wrapping another shape.</summary>
    Nullable,

    /// <summary>A byte array, encoded as base64.</summary>
    Bytes,

    /// <summary>A sequence, encoded as a JSON array.</summary>
    Sequence,

    /// <summary>A string-keyed map, encoded as a JSON object.</summary>
    Dictionary,

    /// <summary>A composite with properties, encoded as a JSON object.</summary>
    Object,

    /// <summary>A <c>RemoteFile</c>, which travels as a multipart part rather than in the JSON.</summary>
    File,

    /// <summary>A type with no wire encoding. <see cref="WireType.Reason" /> says why.</summary>
    Unsupported,
}
