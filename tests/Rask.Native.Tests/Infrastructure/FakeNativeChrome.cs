using System.Text;
using Rask.Native;

namespace Rask.Native.Tests.Infrastructure;

/// <summary>
///     A test double for <see cref="INativeChrome" />: captures each chrome descriptor the session pushes and
///     lets a test raise bar interactions (<see cref="TapAsync" /> for a button, <see cref="NavigateAsync" />
///     for a tab) through <see cref="OnChromeEvent" /> — the same channel a real platform head would drive.
/// </summary>
internal sealed class FakeNativeChrome : INativeChrome
{
    /// <summary>Every descriptor pushed via <see cref="ApplyChromeAsync" />, in order (copied UTF-8 JSON).</summary>
    public List<byte[]> Pushed { get; } = new();

    public Func<byte[], Task>? OnChromeEvent { get; set; }

    public ValueTask ApplyChromeAsync(ReadOnlyMemory<byte> chromeDescriptorUtf8)
    {
        Pushed.Add(chromeDescriptorUtf8.ToArray());
        return default;
    }

    /// <summary>The most recently pushed descriptor as a JSON string.</summary>
    public string LastJson => Encoding.UTF8.GetString(Pushed[^1]);

    /// <summary>Simulate a bar-button tap for the given descriptor id.</summary>
    public Task TapAsync(string id) =>
        OnChromeEvent?.Invoke(Encoding.UTF8.GetBytes($$"""{"type":"nativeTap","id":"{{id}}"}""")) ?? Task.CompletedTask;

    /// <summary>Simulate a tab tap navigating to the given path.</summary>
    public Task NavigateAsync(string path) =>
        OnChromeEvent?.Invoke(Encoding.UTF8.GetBytes($$"""{"type":"navigate","path":"{{path}}"}""")) ?? Task.CompletedTask;
}
