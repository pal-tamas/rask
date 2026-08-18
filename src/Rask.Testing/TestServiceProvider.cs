namespace Rask.Testing;

/// <summary>
///     A minimal <see cref="IServiceProvider" /> for handing a component under test the one or two services
///     it resolves from the container — a <see cref="TestFileBackend" />, a <see cref="TestDownloadSink" />,
///     a <c>Navigator</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>RaskTest.Render</c> takes an <see cref="IServiceProvider" />, and <c>Rask.Testing</c>
///         deliberately depends on no DI container — so without this, every test that needs one service had
///         to either pull in <c>Microsoft.Extensions.DependencyInjection</c> or hand-roll a provider. This is
///         that provider, once.
///     </para>
///     <para>
///         Registrations are by exact type, with no lifetime, scope or resolution rules: whatever you put in
///         is what comes out, and an unregistered type comes back <c>null</c> the way
///         <c>IServiceProvider</c> requires. If a test needs more than that, register a real container's
///         provider instead — <c>RaskTest.Render</c> accepts any <see cref="IServiceProvider" />.
///     </para>
///     <code>
///     var files = new TestFileBackend();
///     var page = RaskTest.Render(new UploadPage(), TestServiceProvider.With&lt;IBrowserFileBackend&gt;(files));
///
///     // several services:
///     var services = new TestServiceProvider()
///         .Add&lt;IBrowserFileBackend&gt;(files)
///         .Add&lt;IDownloadSink&gt;(downloads);
///     </code>
/// </remarks>
public sealed class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.GetValueOrDefault(serviceType);
    }

    /// <summary>A provider holding a single service, for the common one-dependency case.</summary>
    /// <typeparam name="TService">The type the component resolves — usually the interface, not the double's class.</typeparam>
    /// <param name="instance">The instance to hand back.</param>
    public static TestServiceProvider With<TService>(TService instance)
        where TService : class => new TestServiceProvider().Add(instance);

    /// <summary>Registers <paramref name="instance" /> under <typeparamref name="TService" />, replacing any previous one.</summary>
    /// <typeparam name="TService">The type the component resolves — usually the interface, not the double's class.</typeparam>
    /// <param name="instance">The instance to hand back.</param>
    /// <returns>This provider, for chaining.</returns>
    public TestServiceProvider Add<TService>(TService instance)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(TService)] = instance;
        return this;
    }

    /// <summary>
    ///     Registers <paramref name="instance" /> under <paramref name="serviceType" />, for a type only known
    ///     at runtime.
    /// </summary>
    /// <param name="serviceType">The type the component resolves.</param>
    /// <param name="instance">The instance to hand back; must be assignable to <paramref name="serviceType" />.</param>
    /// <returns>This provider, for chaining.</returns>
    public TestServiceProvider Add(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        if (!serviceType.IsInstanceOfType(instance))
        {
            throw new ArgumentException(
                $"{instance.GetType()} is not assignable to {serviceType}.", nameof(instance));
        }

        _services[serviceType] = instance;
        return this;
    }
}
