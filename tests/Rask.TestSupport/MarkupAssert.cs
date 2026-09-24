using System.Text.Json;
using Rask.Testing;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Lookups over rendered Rask HTML / live payloads, on the attribute scanner <c>Rask.Testing</c> keeps internal
///     (this project compiles its own copy of <c>AttributeScan.cs</c>).
/// </summary>
/// <remarks>
///     <para>
///         What is here is what the package deliberately does not ship: helpers that call
///         <c>Assert</c> (<c>Rask.Testing</c> is test-framework-agnostic and must stay so), and helpers
///         shaped around the live wire payload, which is below the HTML + handler-dispatch seam that
///         <c>Rask.Testing</c> covers.
///     </para>
/// /// </remarks>
public static class MarkupAssert
{
    /// <summary>The value of the first <paramref name="name" />=&quot;…&quot; attribute, or <c>null</c>.</summary>
    public static string? Attr(string html, string name) => AttributeScan.Attr(html, name);

    /// <summary>The value of every <paramref name="name" />=&quot;…&quot; attribute, in document order.</summary>
    public static IReadOnlyList<string> Attrs(string html, string name) => AttributeScan.Attrs(html, name);

    /// <summary>
    ///     Returns the value of <paramref name="name" />=&quot;…&quot;, asserting that the
    ///     attribute is present (fails the test otherwise).
    /// </summary>
    public static string RequireAttr(string html, string name)
    {
        var value = AttributeScan.Attr(html, name);
        Assert.True(value is not null, $"no {name} attribute found in html: {html}");
        return value!;
    }

    /// <summary>Returns the <c>data-rask-root</c> session id from a rendered shell.</summary>
    public static string SessionId(string html) => RequireAttr(html, "data-rask-root");

    /// <summary>The first <c>data-rask-on-click</c> handler id in rendered HTML (asserts present).</summary>
    public static string FirstHandlerId(string html) => RequireAttr(html, "data-rask-on-click");

    /// <summary>
    ///     Parses the <c>html</c> field out of a JSON live payload and returns the first
    ///     <c>data-rask-on-click</c> handler id, asserting one is present.
    /// </summary>
    public static string FirstHandlerId(byte[] jsonPayload)
    {
        using var doc = JsonDocument.Parse(jsonPayload.AsMemory());
        return FirstHandlerId(doc.RootElement.GetProperty("html").GetString()!);
    }
}
