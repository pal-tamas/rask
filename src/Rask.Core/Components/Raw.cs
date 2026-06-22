namespace Rask.Core.Components;

/// <summary>
///     Emits its <see cref="Value" /> verbatim, without HTML encoding. This is the framework's
///     only un-encoded output path, so it is an XSS sink: only pass HTML you generate or fully
///     trust. <b>Never</b> bind untrusted input (user text, request data, a download/upload file
///     name) into <see cref="Value" /> — use <see cref="Text" /> (or any element child), which
///     HTML-encodes, for anything a user can influence.
/// </summary>
public sealed class Raw : Component
{
    public Raw() { }
    public Raw(string html) => Value = html;

    /// <summary>The verbatim HTML to emit. Not encoded — see the type remarks on XSS.</summary>
    public string? Value { get; set; }
}
