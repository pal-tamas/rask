using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Asserts that a Rask tree and a Blazor component render to structurally
///     equivalent HTML. "Structurally equivalent" means: same set of opening tags
///     (counted by name) and same total text length, after stripping framework-
///     specific attribute markers. We don't demand byte-equal output — Rask emits
///     <c>data-rask-root</c>, Rask scoped CSS emits <c>data-r-XXXXXXXX</c>, and
///     Blazor's CSS isolation emits <c>_bl_XXXXXXXX</c>; attribute order may also
///     differ.
///     <para>
///         If structural parity fails for a paired benchmark, the benchmark's
///         bytes-on-wire numbers are not directly comparable and the run should
///         fail-fast rather than publish misleading results.
///     </para>
/// </summary>
internal static class ParityCheck
{
    private static readonly Regex RaskScopeAttr = new(@"\s*data-r-[0-9a-f]{8}=""""", RegexOptions.Compiled);
    private static readonly Regex RaskRootAttr = new(@"\s*data-rask-root=""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex BlazorMarker = new(@"\s*_bl_[0-9a-f-]+=""""", RegexOptions.Compiled);
    private static readonly Regex TagOpener = new(@"<([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled);

    private static readonly Lazy<HtmlRenderer> SharedHtmlRenderer = new(() =>
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new HtmlRenderer(services, NullLoggerFactory.Instance);
    });

    public static HtmlRenderer SharedBlazorRenderer => SharedHtmlRenderer.Value;

    /// <summary>
    ///     Render <paramref name="raskTree" /> via Rask and a <typeparamref name="TBlazor" />
    ///     instance with <paramref name="blazorParameters" /> via Blazor's
    ///     <see cref="HtmlRenderer" />, normalize both outputs, and throw
    ///     <see cref="ParityException" /> if their structural fingerprints diverge.
    /// </summary>
    public static void Assert<TBlazor>(
        string scenarioName,
        Component raskTree,
        ParameterView blazorParameters)
        where TBlazor : IComponent
    {
        var raskHtml = raskTree.ToHtml();
        var blazorHtml = SharedHtmlRenderer.Value.Dispatcher
            .InvokeAsync(async () =>
            {
                var root = await SharedHtmlRenderer.Value.RenderComponentAsync<TBlazor>(blazorParameters);
                return root.ToHtmlString();
            })
            .GetAwaiter().GetResult();

        var raskFingerprint = Fingerprint(raskHtml);
        var blazorFingerprint = Fingerprint(blazorHtml);

        if (!FingerprintMatches(raskFingerprint, blazorFingerprint))
        {
            throw new ParityException(
                $"[{scenarioName}] Rask vs Blazor render diverged.\n" +
                $"  Rask tags: {raskFingerprint.TagSummary}\n" +
                $"  Blazor tags: {blazorFingerprint.TagSummary}\n" +
                $"  Rask text len: {raskFingerprint.TotalTextLength}\n" +
                $"  Blazor text len: {blazorFingerprint.TotalTextLength}");
        }
    }

    /// <summary>
    ///     Asserts two Rask trees produce structurally equivalent HTML. Used by the
    ///     stateful-counter scenario to confirm the cached-rows variant renders the
    ///     same output as the rebuild-each-time factory — without that check, a
    ///     regression in <see cref="StatefulLargePageWithCounter" /> would silently
    ///     ship better diff-codec numbers against a divergent tree.
    /// </summary>
    public static void AssertRaskTreesMatch(string scenarioName, Component left, Component right)
    {
        var leftFp = Fingerprint(left.ToHtml());
        var rightFp = Fingerprint(right.ToHtml());

        if (!FingerprintMatches(leftFp, rightFp))
        {
            throw new ParityException(
                $"[{scenarioName}] Rask trees diverged.\n" +
                $"  Left tags: {leftFp.TagSummary}\n" +
                $"  Right tags: {rightFp.TagSummary}\n" +
                $"  Left text len: {leftFp.TotalTextLength}\n" +
                $"  Right text len: {rightFp.TotalTextLength}");
        }
    }

    private static Fp Fingerprint(string html)
    {
        var stripped = RaskScopeAttr.Replace(html, string.Empty);
        stripped = RaskRootAttr.Replace(stripped, string.Empty);
        stripped = BlazorMarker.Replace(stripped, string.Empty);

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TagOpener.Matches(stripped))
        {
            var tag = m.Groups[1].Value;
            tagCounts.TryGetValue(tag, out var n);
            tagCounts[tag] = n + 1;
        }

        // Total length of text-between-tags, used as a proxy for "the rendered text
        // content matches." A real diff of text-node values would be more precise but
        // is overkill — both renders run the same string-formatting code paths.
        var textLen = 0;
        var inTag = false;
        foreach (var c in stripped)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>')
            {
                inTag = false;
            }
            else if (!inTag && !char.IsWhiteSpace(c))
            {
                textLen++;
            }
        }

        var summary = string.Join(",",
            tagCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
        return new Fp(summary, textLen);
    }

    private static bool FingerprintMatches(Fp a, Fp b) =>
        a.TagSummary == b.TagSummary && a.TotalTextLength == b.TotalTextLength;

    private readonly record struct Fp(string TagSummary, int TotalTextLength);

    public sealed class ParityException(string message) : Exception(message);
}
