namespace Rask.Data;

/// <summary>
///     Whether a gate of one kind is open for the work in flight, as the app said with
///     <see cref="RaskDataServiceCollectionExtensions.AddRaskGate{TGate}" />.
/// </summary>
/// <param name="GateType">The kind of gate it answers for.</param>
/// <param name="IsOpen">The answer, given the scope's services (or the app's, outside a scope) and the gate.</param>
internal sealed record GateRule(Type GateType, Func<IServiceProvider, object, bool> IsOpen);
