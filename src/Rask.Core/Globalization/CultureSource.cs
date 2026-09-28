namespace Rask.Core.Globalization;

/// <summary>Where a negotiated culture came from.</summary>
public enum CultureSource
{
    /// <summary>Nothing matched; the configured default was used.</summary>
    Default,

    /// <summary>The visitor's own preference (<c>Accept-Language</c> / <c>navigator.languages</c>).</summary>
    Client,

    /// <summary>A remembered explicit choice.</summary>
    Cookie,

    /// <summary>An explicit override in the URL.</summary>
    Query,
}
