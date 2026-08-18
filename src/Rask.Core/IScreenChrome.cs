namespace Rask.Core;

/// <summary>
///     The serializer's one hook into screen chrome: a component that declares bars around itself rather than
///     only a body. Implemented by <c>Rask.Chrome.Screen</c>.
/// </summary>
/// <remarks>
///     <para>
///         This exists so <c>Rask.Core</c> can walk a screen's chrome slots without naming a single chrome
///         type. The vocabulary — the portable <c>AppBar</c>/<c>TabStrip</c> and the platform-exact
///         <c>Native*</c> bars — lives outside Core entirely; Core only needs to know that this component has
///         three slots and when to serialize them.
///     </para>
///     <para>
///         Internal, and implemented explicitly by <c>Screen</c>, so the slots stay <c>protected</c> on the
///         public surface: a screen's chrome is declared by overriding, not by anyone calling it.
///     </para>
/// </remarks>
internal interface IScreenChrome
{
    /// <summary>The bar above the body, or <c>null</c> for none.</summary>
    Component? HeaderBarSlot { get; }

    /// <summary>The contextual action bar, or <c>null</c> for none.</summary>
    Component? ToolbarSlot { get; }

    /// <summary>The primary navigation bar, or <c>null</c> for none.</summary>
    Component? TabBarSlot { get; }
}
