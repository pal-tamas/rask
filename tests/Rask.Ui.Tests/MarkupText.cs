namespace Rask.UiTests;

/// <summary>Rendered markup with its character references resolved, for a test that reads an attribute's value.</summary>
internal static class MarkupText
{
    /// <summary>
    ///     A <c>style</c> attribute's <c>+</c> and a shortcut's <c>⌘</c> are written as <c>&amp;#x2B;</c> and
    ///     <c>&amp;#x2318;</c>; this is the text a browser reads back.
    /// </summary>
    internal static string AsText(this string html) => System.Net.WebUtility.HtmlDecode(html);
}
