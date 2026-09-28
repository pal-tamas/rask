namespace Rask.Core.Components;

/// <summary>
///     Service contract a host registers to emit extra framework-managed markup inside the
///     server-rendered <c>&lt;head&gt;</c>, alongside the head-asset sentinel. The Server host uses it to
///     emit the PWA <c>&lt;link rel="manifest"&gt;</c> and <c>&lt;meta name="theme-color"&gt;</c> directly
///     as HTML (so no post-boot JS injection is needed, unlike WASM). The contribution is serialized on
///     every render; keep its output byte-stable so the live diff codec never emits ops for it.
///     <para>
///         Optional: when no host registers an <see cref="IRaskHeadContribution" /> (the default, and on
///         WASM), <see cref="HtmlSerializer" /> emits nothing extra.
///     </para>
/// </summary>
public interface IRaskHeadContribution
{
    /// <summary>The markup to splice into <c>&lt;head&gt;</c>, or <c>null</c> to contribute nothing.</summary>
    Component? Render();
}
