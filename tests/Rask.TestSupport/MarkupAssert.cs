using System.Text.Json;
using Rask.Testing;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Asserting lookups over rendered Rask HTML / live payloads, layered on the shipped
///     <see cref="Markup" /> scanner in <c>Rask.Testing</c>.
/// </summary>
/// <remarks>
///     <para>
///         Plain <c>name="…"</c> lookups live in <see cref="Markup" /> — use <c>Markup.Attr(html, name)</c>
///         directly. What is left here is what the package deliberately does not ship: helpers that call
///         <c>Assert</c> (<c>Rask.Testing</c> is test-framework-agnostic and must stay so), and helpers
///         shaped around the live wire payload, which is below the HTML + handler-dispatch seam that
///         <c>Rask.Testing</c> covers.
///     </para>
///     <para>
///         Named <c>MarkupAssert</c>, not <c>Markup</c>: this and <see cref="Markup" /> are both in scope in
///         every project that uses them, so sharing a name would make every unqualified <c>Markup.</c>
///         ambiguous (CS0104).
///     </para>
/// </remarks>
public static class MarkupAssert
{
    /// <summary>
    ///     Returns the value of <paramref name="name" />=&quot;…&quot;, asserting that the
    ///     attribute is present (fails the test otherwise).
    /// </summary>
    public static string RequireAttr(string html, string name)
    {
        var value = Markup.Attr(html, name);
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
