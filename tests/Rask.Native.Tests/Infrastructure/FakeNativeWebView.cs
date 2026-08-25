using System.Text;
using Rask.Native;

namespace Rask.Native.Tests.Infrastructure;

/// <summary>
///     A test double for <see cref="INativeWebView" />: captures the frames the session pushes and the JS
///     the runtime evaluates, and lets a test feed WebView → .NET messages through <see cref="PostAsync" />
///     (the same channel a real <c>WKScriptMessageHandler</c> / <c>[JavascriptInterface]</c> would drive).
/// </summary>
internal sealed class FakeNativeWebView : INativeWebView
{
    /// <summary>Every frame the host pushed via <see cref="ApplyRenderAsync" />, in order (copied).</summary>
    public List<byte[]> Frames { get; } = new();

    /// <summary>Every script the host evaluated via <see cref="EvaluateJavaScriptAsync" /> (IJSRuntime interop).</summary>
    public List<string> Evaluated { get; } = new();

    /// <summary>
    ///     Every address the session navigated to via <see cref="LoadUrlAsync" />, in order — a Url-mode
    ///     <c>NativeWebView</c>. The count is the point as much as the values: a re-render naming the same
    ///     address must not reload the page.
    /// </summary>
    public List<Uri> LoadedUrls { get; } = new();

    /// <summary>What the host advertised to the page — the derived native-backend set.</summary>
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    public Func<byte[], Task>? OnMessage { get; set; }

    public ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        // Copy now — the base swaps the write buffer after this returns.
        Frames.Add(frameUtf8.ToArray());
        return default;
    }

    public ValueTask EvaluateJavaScriptAsync(string javaScript)
    {
        Evaluated.Add(javaScript);
        return default;
    }

    public ValueTask LoadUrlAsync(Uri url)
    {
        LoadedUrls.Add(url);
        return default;
    }

    /// <summary>The most recently pushed frame.</summary>
    public byte[] LastFrame => Frames[^1];

    /// <summary>Drive a WebView → .NET message (an event / navigate / ready / jsResult).</summary>
    public Task PostAsync(string json) => OnMessage?.Invoke(Encoding.UTF8.GetBytes(json)) ?? Task.CompletedTask;
}
