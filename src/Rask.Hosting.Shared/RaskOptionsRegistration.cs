using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Rask.Hosting.Shared;

/// <summary>
/// Registers a Rask options type so it reads its <c>Rask:&lt;Area&gt;</c> configuration section before the code
/// callback runs — the one place every server-side <c>AddRaskX</c> gets "appsettings first, code on top".
/// </summary>
/// <remarks>
/// <para>
/// <b>Order</b>, which is the whole contract: type defaults → the caller's <c>defaults</c> →
/// configuration (appsettings, environment variables, user secrets) → the code callback → validation. Options
/// setups run in registration order, so registering them in exactly that order is what makes code win over
/// configuration and configuration win over a battery's defaults.
/// </para>
/// <para>
/// <b>Nothing is read at registration.</b> A host registers <see cref="IConfiguration"/> through a factory, so
/// it is not in the collection to be read yet — and sources an app adds after <c>AddRaskX</c> must still count.
/// The section is bound when the options are first built, from whatever <see cref="IConfiguration"/> the
/// container has. A container with none (a test harness composing a bare <c>ServiceCollection</c>) skips the
/// bind and gets the defaults plus the callback, the same thing it got before configuration existed.
/// </para>
/// <para>
/// <b>The bind is a lambda written at the call site</b> — <c>static (section, o) =&gt; section.Bind(o)</c> —
/// rather than a <c>Bind</c> call in here. The configuration binding source generator intercepts a call whose
/// target type it can see; inside this generic helper it would see only <c>T</c> and fall back to the
/// reflection binder, which the trimmer cannot follow.
/// </para>
/// <para>
/// <b>Consumers are unchanged.</b> Every battery injected its options as the plain type, so the plain type is
/// registered as a view of <see cref="IOptions{TOptions}"/>. A bad value fails the host's start through
/// <c>ValidateOnStart</c>, naming the section, instead of the first request that happens to need it.
/// </para>
/// </remarks>
internal static class RaskOptionsRegistration
{
    /// <summary>
    /// Registers <typeparamref name="T"/> bound from <paramref name="section"/>, then <paramref name="configure"/>,
    /// then <paramref name="validate"/> at start. Returns <c>false</c> — registering nothing — when
    /// <typeparamref name="T"/> was already registered this way: the first <c>AddRaskX</c> call wins, as it
    /// always has.
    /// </summary>
    internal static bool AddRaskOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        this IServiceCollection services,
        string section,
        Action<IConfigurationSection, T> bind,
        Action<T>? configure,
        Action<T>? validate,
        Action<T>? defaults = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentNullException.ThrowIfNull(bind);

        // Without the marker, a second AddRaskX would append a second bind and a second callback, and the
        // options would silently become the merge of both calls rather than the first one.
        if (services.Any(static d => d.ServiceType == typeof(Marker<T>)))
        {
            return false;
        }

        services.AddSingleton(new Marker<T>());

        var builder = services.AddOptions<T>();
        if (defaults is not null)
        {
            builder.Configure(defaults);
        }

        services.AddSingleton<IConfigureOptions<T>>(sp =>
            new BindSection<T>(section, sp.GetService<IConfiguration>(), bind));

        if (configure is not null)
        {
            builder.Configure(configure);
        }

        if (validate is not null)
        {
            services.AddSingleton<IValidateOptions<T>>(new ValidateWith<T>(section, validate));
        }

        builder.ValidateOnStart();
        services.TryAddSingleton<T>(static sp => sp.GetRequiredService<IOptions<T>>().Value);
        return true;
    }

    /// <summary>
    /// Builds <typeparamref name="T"/> right now from <paramref name="services"/>' configuration, then
    /// <paramref name="configure"/> — for options that are read while endpoints are mapped and never live in DI.
    /// </summary>
    internal static T BindNow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        IServiceProvider services,
        string section,
        Action<IConfigurationSection, T> bind,
        Action<T>? configure)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new T();
        new BindSection<T>(section, services.GetService<IConfiguration>(), bind).Configure(options);
        configure?.Invoke(options);
        return options;
    }

    /// <summary>
    /// The connection string named <paramref name="name"/> under <c>Rask:ConnectionStrings</c> — or an error that
    /// says exactly where to put one, in both the JSON and the environment-variable spelling.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fallback. A database-backed battery that quietly opened <c>app.db</c> in the working
    /// directory is how a container writes its data somewhere the next deploy deletes; a scaffolded app carries the
    /// key in its appsettings.json and <c>rask deploy</c> sets it, so its absence means something is misconfigured.
    /// </remarks>
    internal static string ConnectionString(IServiceProvider services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var value = services.GetService<IConfiguration>()?[$"Rask:ConnectionStrings:{name}"];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"No connection string named '{name}'. Set Rask:ConnectionStrings:{name} in appsettings.json, or "
                + $"Rask__ConnectionStrings__{name} in the environment.")
            : value;
    }

    // One per options type, marking that AddRaskOptions already ran for it on this collection.
    private sealed class Marker<T>;

    private sealed class BindSection<T>(string section, IConfiguration? configuration, Action<IConfigurationSection, T> bind)
        : IConfigureOptions<T>
        where T : class
    {
        public void Configure(T options)
        {
            if (configuration is null)
            {
                return;
            }

            try
            {
                bind(configuration.GetSection(section), options);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                // A setter that rejects a value (a prefix without its leading slash) or a value the binder
                // cannot convert ("five" for a TimeSpan) throws from deep inside the bind. Rethrown as a
                // validation failure so it reads like every other bad setting: which section, and why.
                throw new OptionsValidationException(
                    Options.DefaultName, typeof(T), [$"{section}: {ex.Message}"]);
            }
        }
    }

    private sealed class ValidateWith<T>(string section, Action<T> validate) : IValidateOptions<T>
        where T : class
    {
        public ValidateOptionsResult Validate(string? name, T options)
        {
            // Only the unnamed instance is the one AddRaskX registered; a named instance is someone else's.
            if (name is not null && name != Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            try
            {
                validate(options);
                return ValidateOptionsResult.Success;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return ValidateOptionsResult.Fail($"{section}: {ex.Message}");
            }
        }
    }
}
