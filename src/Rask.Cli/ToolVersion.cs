namespace Rask.Cli;

/// <summary>Reads a version out of whatever a tool printed for its <c>--version</c>.</summary>
internal static class ToolVersion
{
    /// <summary>
    ///     The version in <paramref name="reported" />, or <see langword="null" /> when there is none.
    ///     <c>dotnet --version</c> can answer <c>10.0.100-preview.3.25201.16</c>, and other tools lead with a
    ///     <c>v</c> — so the leading <c>v</c> and any pre-release suffix are both stripped before parsing
    ///     rather than being allowed to fail the parse and report a present tool as missing.
    /// </summary>
    public static Version? Parse(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return null;
        }

        var span = reported.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        var cut = span.AsSpan().IndexOfAny('-', '+', ' ');
        if (cut >= 0)
        {
            span = span[..cut];
        }

        // Version.TryParse rejects a bare major ("10"), which is a shape `dotnet --list-sdks` style
        // output can take, so a single component is padded rather than discarded.
        if (!span.Contains('.'))
        {
            span += ".0";
        }

        return Version.TryParse(span, out var version) ? version : null;
    }
}
