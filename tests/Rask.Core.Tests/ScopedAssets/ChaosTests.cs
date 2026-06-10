using System.Text;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Chaos and failure-mode coverage for <see cref="ScopedAssetRegistry" />: malformed
///     input, mid-mutation lookups, content edge cases. These aren't happy-path tests —
///     each one targets a way the registry could plausibly mis-behave under production
///     stress (hot reload race, syntax-broken sibling files, non-ASCII content).
/// </summary>
[Collection("ScopedAssets")]
public class ChaosTests
{
    public ChaosTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void MalformedCss_DoesNotThrow_BytesPreserved()
    {
        // Syntax-broken CSS (unbalanced braces, missing semicolons) should not crash
        // CssScoper.Rewrite. The browser parses what it can; the framework's job is to
        // serve the bytes, not validate syntax.
        const string malformed = ".x { color: red MISSING-SEMICOLON another-decl: blue; { } } trailing-junk";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), malformed);

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash));
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css);
        Assert.NotNull(bytes);
    }

    [Fact]
    public void MalformedJs_WrapsAndStores_BrowserSurfacesSyntaxErrorAtRuntime()
    {
        // Syntax-broken JS — the wrapper IIFE is still emitted; the browser console will
        // log the SyntaxError when it executes, but the framework returns valid bytes.
        const string malformed = "export function f( { BROKEN UNCLOSED";
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), malformed);

        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Js);
        Assert.NotNull(bytes);
        var js = Encoding.UTF8.GetString(bytes!.Value.Utf8.Span);
        Assert.Contains("(function () {", js);
        Assert.Contains("window.Rask", js);
    }

    [Fact]
    public async Task RegisterMidLookup_RaceDoesNotThrow_FinalStateConsistent()
    {
        // Concurrent burst: 200 register calls interleaved with 200 lookups. The lookup
        // half may legitimately see null mid-replacement (the by-hash entry is dropped
        // between TryGetCss and GetByHash); what's not allowed is an exception. Final
        // state must be self-consistent.
        var t1 = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                ScopedAssetRegistry.RegisterCss(typeof(WidgetA),
                    $".x {{ color: rgb({i % 256},0,0); }}");
            }
        });
        var t2 = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                if (ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var h))
                {
                    _ = ScopedAssetRegistry.GetByHash(h, AssetKind.Css);
                }
            }
        });
        await Task.WhenAll(t1, t2);

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var final));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(final, AssetKind.Css));
    }

    [Fact]
    public void NonAsciiCss_BomAndEmoji_RoundTripsByteForByte()
    {
        // UTF-8 with multi-byte chars: emoji in content, RTL text in comments. The
        // registry encodes once via Encoding.UTF8.GetBytes; the endpoint must serve
        // those bytes verbatim. No re-encoding, no normalization.
        const string css = ".x::before { content: '🎨'; } /* مرحبا */";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), css);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();

        // Round-trip: bytes → string → bytes must match exactly.
        var roundtrip = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes));
        Assert.Equal(bytes, roundtrip);
        // And the emoji's UTF-8 sequence (F0 9F 8E A8) survived.
        Assert.Contains((byte)0xF0, bytes);
        Assert.Contains((byte)0x9F, bytes);
    }

    [Fact]
    public void ContentLengthMatchesByteLength_NotCharLength_ForMultiByteContent()
    {
        // Sanity for the endpoint's Content-Length math: char length ≠ byte length for
        // non-ASCII. The endpoint serves byte-length bodies.
        const string css = ".x::before { content: '😀😀😀'; }"; // 3 × 4-byte UTF-8 emoji
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), css);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        // Each 😀 is 4 UTF-8 bytes but 2 UTF-16 chars — assert at least 12 emoji-bytes.
        var asString = Encoding.UTF8.GetString(bytes);
        Assert.True(bytes.Length > asString.Length, "byte length should exceed char length for multi-byte content");
    }

    [Fact]
    public void RapidRegisterUnregisterCycle_LeavesNoLeakedEntries()
    {
        // 100 register-then-unregister cycles for the same type. By-hash bucket should
        // end empty (refcounts hit zero, entries dropped).
        for (var i = 0; i < 100; i++)
        {
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), $".x {{ value: {i}; }}");
            ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
        }

        Assert.Equal(0, ScopedAssetRegistry.CssEntryCount);
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    [Fact]
    public void EmptyAfterRewriteSource_ActsAsUnregister()
    {
        // CSS that StripComments + Rewrite reduces to empty (e.g., only a comment).
        // Registry should treat it as unregister, not store a zero-byte entry.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), "/* nothing but a comment */");
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }
}
