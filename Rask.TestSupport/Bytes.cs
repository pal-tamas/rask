using System.Text;

namespace Rask.TestSupport;

/// <summary>Byte helpers for building test payloads.</summary>
public static class Bytes
{
    /// <summary>UTF-8 encodes a string — the canonical <c>Utf8(json)</c> test helper.</summary>
    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
