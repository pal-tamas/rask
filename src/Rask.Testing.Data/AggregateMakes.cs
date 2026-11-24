using System.Diagnostics.CodeAnalysis;
using Rask.Data;

namespace Rask.Testing;

/// <summary>
///     <c>await Test.Make&lt;Supply&gt;()</c>: a stored aggregate its rules accept, made through its own factory, with
///     everything it refers to made and stored first.
/// </summary>
public static class AggregateMakes
{
    private const DynamicallyAccessedMemberTypes Kept =
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
        | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;

    extension(Test)
    {
        /// <summary>
        ///     A valid <typeparamref name="T" />, stored in the test's database, for a test that needs one and does not care
        ///     which: <c>var supply = await Test.Make&lt;Supply&gt;();</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The aggregate is made the way the app makes one — through its own factory, a static method declared on
        ///         it that returns it (or a task of it), private or not — and saved as <c>supply.Save()</c> saves it, its
        ///         rules asked. Each parameter gets a value its rules take: a value object made by
        ///         <c>Test.Make&lt;Vin&gt;()</c>, a text, a number, today, an enum's first member, a row spec built through its
        ///         constructor, a list of one row. A nullable parameter is left null, and one with a default keeps it.
        ///     </para>
        ///     <para>
        ///         What it refers to is made first, the same way, and stored: a parameter that is an aggregate
        ///         (<c>Company company</c>), a typed id (<c>TankId tank</c> is a <c>Tank</c>'s), or a plain id named after
        ///         its aggregate (<c>Guid companyId</c>). Each is made once per call, so a supply and the tank its part names
        ///         share one company; <c>o.Use(acme)</c> hands over one the test made instead.
        ///     </para>
        ///     <para>
        ///         A test that cares about a value says so: <c>o.With("parts", parts)</c> by the factory's parameter name,
        ///         <c>o.Factory = nameof(Supply.Register)</c> when the aggregate has several.
        ///     </para>
        /// </remarks>
        /// <typeparam name="T">The aggregate.</typeparam>
        /// <param name="configure">What the test says about it: a factory, its own values, aggregates to refer to.</param>
        /// <returns>The stored <typeparamref name="T" />.</returns>
        /// <exception cref="InvalidOperationException">
        ///     <typeparamref name="T" /> has no factory (or several and none named), a parameter has no sample, or its
        ///     rules refuse what was made; the message names it and the option that helps.
        /// </exception>
        public static async Task<T> Make<[DynamicallyAccessedMembers(Kept)] T>(Action<MakeOptions>? configure = null)
            where T : class, IAggregate
        {
            var options = new MakeOptions();
            configure?.Invoke(options);
            return (T)await new AggregateSample(options, typeof(T)).Make(typeof(T)).ConfigureAwait(false);
        }
    }
}
