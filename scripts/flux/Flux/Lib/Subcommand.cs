namespace Rask.Flux;

/// <summary>
///     One command run by another, as a script once ran another script (<c>spawnSync(parity.mjs, …)</c>): it prints to the
///     same console and answers its exit code — what it would have exited with, its <see cref="FluxExit" /> included.
/// </summary>
public static class Subcommand
{
    /// <summary>Runs <typeparamref name="T" /> on <paramref name="args" />; answers its exit code.</summary>
    public static async Task<int> Run<T>(params string[] args)
        where T : IFluxCommand, new()
    {
        try
        {
            return await new T().Run(args);
        }
        catch (FluxExit exit)
        {
            if (exit.Message.Length > 0)
            {
                await Console.Error.WriteLineAsync(exit.Message);
            }

            return exit.Code;
        }
    }
}
