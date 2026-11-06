using System.Reflection;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>
///     A state a component is only in once it is USED (typed into, opened), reached the same way on both pages before
///     they are measured: <c>parity &lt;slug&gt; --prepare &lt;name&gt;</c>. Mark the class
///     <see cref="ParityPreparationAttribute" />; the parity command finds it by that name.
/// </summary>
public interface IParityPreparation
{
    /// <summary>
    ///     Flux's page it is measured on, for the parity run asked about <paramref name="slug" />: a component's slug, or a
    ///     page of its own with a slash (<c>demo/account-overview</c>).
    /// </summary>
    string Flux(string slug);

    /// <summary>The runtime modules the Rask page is given, as an app has them (<see cref="Runtime" />).</summary>
    IReadOnlyList<string> Runtime { get; }

    /// <summary>How the browser is launched for it, or null for the default.</summary>
    BrowserTypeLaunchOptions? Launch => null;

    /// <summary>Drives one page — <paramref name="side" /> <c>flux</c> or <c>rask</c> — into the state, in <paramref name="scheme" />.</summary>
    Task Prepare(IPage page, string scheme, string side);
}

/// <summary>Names a preparation: what <c>--prepare</c> takes (the script it was, less <c>.mjs</c>: <c>command-open</c>).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ParityPreparationAttribute(string name) : Attribute
{
    /// <summary>The name.</summary>
    public string Name { get; } = name;
}

/// <summary>Finds a preparation by its name.</summary>
public static class ParityPreparations
{
    /// <summary>
    ///     The preparation <paramref name="asked" /> names. A path is read as the script it was —
    ///     <c>scripts/flux/command-open.mjs</c> is <c>command-open</c> — so a command line written for the scripts still runs.
    /// </summary>
    public static IParityPreparation Find(string asked)
    {
        var name = Path.GetFileNameWithoutExtension(asked);
        var type = Assembly.GetExecutingAssembly().GetTypes()
            .FirstOrDefault(t => t.GetCustomAttribute<ParityPreparationAttribute>()?.Name == name && typeof(IParityPreparation).IsAssignableFrom(t));
        return type is null
            ? throw new FluxExit(1, $"flux parity: no preparation \"{name}\"")
            : (IParityPreparation)Activator.CreateInstance(type)!;
    }
}
