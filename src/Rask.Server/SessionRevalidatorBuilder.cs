using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server;

/// <summary>
///     The steps after <c>AddSessionRevalidator&lt;T&gt;()</c>: how long the app's answer about a principal is kept,
///     and which saves drop it.
/// </summary>
/// <remarks>
///     <code>
/// builder.Services.AddSessionRevalidator&lt;AccountCheck&gt;().Keep(5.Seconds).ForgetOnSave&lt;User&gt;();
///     </code>
///     With neither step every event of a live page asks the app, as Rask.Auth's own revalidator is asked.
/// </remarks>
public sealed class SessionRevalidatorBuilder
{
    private readonly SessionUserKeeping _keeping;

    internal SessionRevalidatorBuilder(IServiceCollection services, SessionUserKeeping keeping)
    {
        Services = services;
        _keeping = keeping;
    }

    /// <summary>The service collection <c>AddSessionRevalidator</c> registered into, to go on registering with.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Keeps the app's answer for <paramref name="duration" />: within it, every event of every page whose
    ///     principal carries the same claims (<c>iat</c> and <c>nbf</c> aside) is answered without asking the app again.
    /// </summary>
    /// <param name="duration">How long — <c>5.Seconds</c>. Zero keeps nothing.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     What it costs is how long a change made elsewhere — another instance of the app, a hand-run SQL update —
    ///     can go unseen by a page that is open. A save in this process through Rask.Data is seen at once with
    ///     <see cref="ForgetOnSave{TEntity}" />; a change no save says — another chosen tenant — with
    ///     <see cref="Sessions.Forget(System.Security.Claims.ClaimsPrincipal)" />.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is negative.</exception>
    public SessionRevalidatorBuilder Keep(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _keeping.Keep = duration;
        return this;
    }

    /// <summary>
    ///     Drops every kept answer the moment a save of a <typeparamref name="TEntity" /> commits in this process —
    ///     from a page, a request or a background job — so a user disabled or given another role here is asked
    ///     about afresh at their next event.
    /// </summary>
    /// <typeparam name="TEntity">
    ///     The app's user aggregate (or what is saved when a user changes). A child entity is reported as its aggregate,
    ///     so name the aggregate.
    /// </typeparam>
    /// <returns>The same builder.</returns>
    /// <remarks>Heard through Rask.Data's save path, after the commit and never on a rollback.</remarks>
    public SessionRevalidatorBuilder ForgetOnSave<TEntity>()
        where TEntity : class
    {
        _keeping.ForgetOnSave(typeof(TEntity));
        return this;
    }
}
