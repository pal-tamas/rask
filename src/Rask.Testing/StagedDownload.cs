namespace Rask.Testing;

/// <summary>One download a component handed to <see cref="TestDownloadSink" />.</summary>
/// <param name="FileName">The name the component asked the browser to save it as.</param>
/// <param name="Bytes">The content, materialized — safe to assert on after the component disposed its source.</param>
/// <param name="ContentType">The MIME type, or <c>null</c> when the component left it to the host.</param>
public sealed record StagedDownload(string FileName, byte[] Bytes, string? ContentType)
{
    /// <summary>The content decoded as UTF-8 — the common case for a CSV, JSON or text export.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
}
