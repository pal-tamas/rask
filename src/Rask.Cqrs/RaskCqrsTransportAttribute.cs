namespace Rask.Cqrs;

/// <summary>
///     Marks an assembly as providing a Rask.Cqrs <b>remote transport</b> — the thing that can carry a
///     message to a handler in another process.
/// </summary>
/// <remarks>
///     <para>
///         The source generator emits wire codecs only for a compilation that references an assembly
///         carrying this attribute. That gate is what keeps the feature from reaching code that never
///         asked for it: an app using Rask.Cqrs purely in-process — which is every app using it today —
///         references no transport, so no codec is generated for its messages and none of the
///         constraints a codec implies (RASK053) apply to them.
///     </para>
///     <para>
///         Applied by <c>Rask.Cqrs.Client</c> and <c>Rask.Cqrs.Server</c>. It is public so that a
///         third-party transport can opt into the same code generation.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class RaskCqrsTransportAttribute : Attribute;
