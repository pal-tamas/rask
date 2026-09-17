namespace Rask.Data;

/// <summary>
///     Which generated writes an aggregate takes a <b>form model</b> for.
/// </summary>
/// <remarks>
///     <para>
///         Declare it as a <c>const</c> named <c>Writes</c> on the aggregate to narrow what is generated:
///     </para>
///     <example>
///         <code>
///         public sealed class Passkey : Aggregate&lt;Guid&gt;
///         {
///             public const ModelWrites Writes = ModelWrites.None;   // no PasskeyModel, no form writes
///         }
///         </code>
///     </example>
///     <para>
///         <b>It reaches the form surface and nothing else.</b> The behaviour writes —
///         <c>CreateAsync(p =&gt; …)</c>, <c>UpdateAsync(id, p =&gt; …)</c> and <c>DeleteAsync(id)</c> — are
///         always generated, because none of them takes a model. So is the read face (<c>Passkey.Read</c>),
///         which is not negotiable: querying works through read models, so an aggregate that could switch its
///         read face off would be an aggregate nothing can read.
///     </para>
///     <para>
///         A <c>const</c> rather than an attribute or a static property on purpose: C# itself refuses a
///         non-constant initializer, so the value is always there to be read at compile time and the generator
///         can never quietly fail to find it and emit the whole surface anyway.
///     </para>
///     <para>
///         Leaving the const off means <see cref="All" />, so an aggregate that says nothing is unchanged.
///     </para>
/// </remarks>
[Flags]
public enum ModelWrites
{
    /// <summary>
    ///     No form model and no write that takes one. <c>Passkey.CreateAsync(model)</c>,
    ///     <c>UpdateAsync(id, model)</c>, <c>ModelAsync(id)</c>, <c>ToModel()</c> and the
    ///     <c>PasskeyModel</c> type itself are not generated.
    /// </summary>
    /// <remarks>
    ///     For an aggregate a form has no business creating — one a ceremony owns (a passkey, a session), or
    ///     one only the application itself ever writes.
    /// </remarks>
    None = 0,

    /// <summary>The model, and <c>CreateAsync(model)</c> — a row a form may make but never edit.</summary>
    Create = 1,

    /// <summary>
    ///     The model, and <c>UpdateAsync(id, model)</c> — a row a form may edit but never make, because
    ///     something else decides when one exists.
    /// </summary>
    Update = 2,

    /// <summary>Both, and the model with them. The default when no <c>Writes</c> const is declared.</summary>
    All = Create | Update,
}
