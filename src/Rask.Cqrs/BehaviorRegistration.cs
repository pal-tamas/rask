using System.Diagnostics.CodeAnalysis;

namespace Rask.Cqrs;

// Carries a behavior's DI shape with the trimmer annotation preserved on the implementation type — a
// plain ValueTuple<Type, Type> can't hold a [DynamicallyAccessedMembers] annotation, so the constructor
// requirement would be lost and the WASM publish would warn (IL2077).
internal sealed class BehaviorRegistration
{
    public BehaviorRegistration(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
    }

    public Type ServiceType { get; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type ImplementationType { get; }
}
