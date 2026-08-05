using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Core.Live;

/// <summary>
///     A small, explicitly-declared bag of state that outlives the live session holding it.
/// </summary>
/// <remarks>
///     <para>
///         A live session is a component tree, a DI scope and a set of cancellation tokens — none of which
///         can be serialized. So a session cannot be moved or saved: when the process that owns it goes
///         away (a restart, a redeploy, a reconnect routed to another node), the tree goes with it.
///     </para>
///     <para>
///         What <em>can</em> travel is a record of what the page would need to be built again. That is what
///         this is: the values you name here are carried across, and the page is <b>rebuilt</b> around
///         them rather than resumed. Anything you don't name is gone — in-flight async work, undeclared
///         fields, open interop handles. Declare the state a user would be annoyed to lose (a filter, a
///         wizard step, an unsaved draft), and let everything else come back from the database.
///     </para>
///     <para>
///         Inject it through the constructor, not a settable property — a non-nullable settable property
///         becomes a required factory parameter (RASK002).
///     </para>
///     <para>
///         Keep it small. The bag is capped (16 KB by default across all keys); a session that exceeds
///         the cap keeps working but declares itself unresumable, so it falls back to the reload it would
///         have had anyway. This is a place for identifiers and selections, not for cached rows.
///     </para>
///     <para>
///         The reflection-based overloads are the ergonomic default; the <see cref="JsonTypeInfo{T}" />
///         overloads are trim-/AOT-safe (supply a source-generated <c>JsonSerializerContext</c>) — the same
///         pairing <c>ICache</c> uses.
///     </para>
/// </remarks>
public interface IPersistentState
{
    /// <summary>Records <paramref name="value" /> under <paramref name="key" />, replacing any previous value.</summary>
    [RequiresUnreferencedCode(PersistentState.TrimWarning)]
    [RequiresDynamicCode(PersistentState.TrimWarning)]
    void Persist<T>(string key, T value);

    /// <summary>Records <paramref name="value" /> using a source-generated <paramref name="typeInfo" /> (trim-/AOT-safe).</summary>
    void Persist<T>(string key, T value, JsonTypeInfo<T> typeInfo);

    /// <summary>
    ///     Reads back a value recorded before the page was rebuilt. Returns <c>false</c> when the key was
    ///     never written, and when the stored JSON cannot be read as <typeparamref name="T" /> at all —
    ///     which is what a deploy that changed the type behind a key looks like from here. Treat a
    ///     <c>false</c> as "no value", never as an error.
    /// </summary>
    /// <remarks>
    ///     A shape that is merely <em>different</em> rather than unreadable is not a miss: System.Text.Json
    ///     fills what it cannot find with defaults, so renaming a property on a persisted type gives you a
    ///     successfully-read object with a null in it, from a token the previous deploy wrote. If you need
    ///     to change a persisted type, change its key too — that turns an ambiguous half-read into a clean
    ///     miss you can handle.
    /// </remarks>
    [RequiresUnreferencedCode(PersistentState.TrimWarning)]
    [RequiresDynamicCode(PersistentState.TrimWarning)]
    bool TryGet<T>(string key, out T? value);

    /// <summary>Reads back a value using a source-generated <paramref name="typeInfo" /> (trim-/AOT-safe).</summary>
    bool TryGet<T>(string key, JsonTypeInfo<T> typeInfo, out T? value);

    /// <summary>Drops <paramref name="key" />. Returns <c>true</c> if it was present.</summary>
    bool Remove(string key);

    /// <summary>Drops every key.</summary>
    void Clear();
}
