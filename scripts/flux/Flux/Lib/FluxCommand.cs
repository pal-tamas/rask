namespace Rask.Flux;

/// <summary>One check or generator of the harness, run as <c>flux &lt;name&gt; [args]</c>.</summary>
public interface IFluxCommand
{
    /// <summary>Runs the command on its arguments (the command's name already taken off); answers the exit code.</summary>
    Task<int> Run(string[] args);
}

/// <summary>Names a command: the name it is run by — the script it was, less <c>.mjs</c> — and a line for the listing.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FluxCommandAttribute(string name, string summary) : Attribute
{
    /// <summary>What the command is run by: <c>flux parity button</c>.</summary>
    public string Name { get; } = name;

    /// <summary>One line of what it does.</summary>
    public string Summary { get; } = summary;
}

/// <summary>Ends the run with <paramref name="code" />, after printing <paramref name="message" /> to stderr.</summary>
public sealed class FluxExit(int code, string message = "") : Exception(message)
{
    /// <summary>The process's exit code.</summary>
    public int Code { get; } = code;
}
