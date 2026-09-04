using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
/// Configures <c>AddRaskCqrs</c>: the lifetime discovered handlers are registered at, how
/// notifications fan out, and the pipeline behaviors (decorators) to apply. Handlers themselves are
/// discovered by the source generator — you never list them here.
/// </summary>
public sealed class CqrsOptions
{
    /// <summary>
    /// The <see cref="ServiceLifetime"/> the source-generated handler and the registered behaviors are
    /// added at. Defaults to <see cref="ServiceLifetime.Transient"/> (handlers are cheap and stateless).
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>How a notification's handlers are run. Defaults to <see cref="NotificationPublishStrategy.Sequential"/>.</summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; } = NotificationPublishStrategy.Sequential;

    /// <summary>
    /// When running notifications <see cref="NotificationPublishStrategy.Sequential"/>ly, whether the
    /// first handler failure stops the run and rethrows (default), or every handler runs and failures
    /// are collected into an <see cref="AggregateException"/>.
    /// </summary>
    public bool StopOnFirstNotificationException { get; set; } = true;

    /// <summary>
    ///     Whether every dispatched request is validated before its handler runs — its
    ///     <c>System.ComponentModel.DataAnnotations</c> attributes, any <c>AbstractValidator&lt;T&gt;</c>
    ///     written for it, and any <see cref="IRequestValidator{TRequest}" /> registered for it.
    ///     <see langword="true" /> by default.
    ///     <para>
    ///         An app hosted by the <c>Rask</c> package says this as
    ///         <c>app.Configure(c =&gt; c.Validation.Off())</c>, which sets this.
    ///     </para>
    /// </summary>
    public bool ValidateRequests { get; set; } = true;

    internal List<BehaviorRegistration> Behaviors { get; } = [];

    /// <summary>
    /// Registers an <b>open-generic</b> <see cref="IPipelineBehavior{TRequest, TResult}"/> that wraps
    /// every request — the common case for cross-cutting logging/validation. Behaviors run in
    /// registration order (first-registered is outermost).
    /// </summary>
    /// <param name="openBehaviorType">An open generic type definition implementing <c>IPipelineBehavior&lt;,&gt;</c>.</param>
    public CqrsOptions AddOpenBehavior(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        if (!openBehaviorType.IsGenericTypeDefinition || openBehaviorType.GetGenericArguments().Length != 2)
        {
            throw new ArgumentException(
                $"'{openBehaviorType}' must be an open generic type with two type parameters, e.g. typeof(MyBehavior<,>).",
                nameof(openBehaviorType));
        }

        if (!ImplementsOpenPipelineBehavior(openBehaviorType))
        {
            throw new ArgumentException(
                $"'{openBehaviorType}' must implement IPipelineBehavior<TRequest, TResult>.",
                nameof(openBehaviorType));
        }

        Behaviors.Add(new BehaviorRegistration(typeof(IPipelineBehavior<,>), openBehaviorType));
        return this;
    }

    /// <summary>
    /// Registers a <b>closed</b> <see cref="IPipelineBehavior{TRequest, TResult}"/> that wraps only the
    /// given request/result pair. Behaviors run in registration order (first-registered is outermost).
    /// </summary>
    public CqrsOptions AddBehavior<TRequest, TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
        where TImplementation : class, IPipelineBehavior<TRequest, TResult>
    {
        Behaviors.Add(new BehaviorRegistration(typeof(IPipelineBehavior<TRequest, TResult>), typeof(TImplementation)));
        return this;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(HandlerLifetime))
        {
            throw new InvalidOperationException($"{nameof(HandlerLifetime)} has an invalid value: {HandlerLifetime}.");
        }

        if (!Enum.IsDefined(NotificationPublishStrategy))
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationPublishStrategy)} has an invalid value: {NotificationPublishStrategy}.");
        }
    }

    private static bool ImplementsOpenPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type openBehaviorType)
    {
        foreach (var i in openBehaviorType.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            {
                return true;
            }
        }

        return false;
    }
}

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
