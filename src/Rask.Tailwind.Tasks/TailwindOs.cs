namespace Rask.Tailwind.Tasks;

/// <summary>The platforms Tailwind publishes a standalone binary for.</summary>
/// <remarks>
///     Public only so the tests can name it in a theory. This assembly is build-only — it is loaded by
///     a UsingTask and never referenced by a consumer — so there is no API surface to protect.
/// </remarks>
public enum TailwindOs
{
    MacOs,
    Linux,
    Windows,
}
