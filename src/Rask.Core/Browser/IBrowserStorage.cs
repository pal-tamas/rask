namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the browser's two Web Storage areas. Inject it through a component constructor
///     (<c>public MyPage(IBrowserStorage storage)</c>) and call from an event handler or lifecycle hook:
///     <code>
///     await storage.Local.SetAsync("theme", "dark");
///     var theme = await storage.Local.GetAsync("theme");
///     </code>
///     Identical on Server and WASM — both resolve to the same <see cref="IWebStorage" /> surface over
///     the unified <c>IJSRuntime</c>.
/// </summary>
public interface IBrowserStorage
{
    /// <summary>Persistent storage that survives across browser sessions (<c>window.localStorage</c>).</summary>
    IWebStorage Local { get; }

    /// <summary>Per-tab storage cleared when the page session ends (<c>window.sessionStorage</c>).</summary>
    IWebStorage Session { get; }
}
