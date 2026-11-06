using System.Reflection;
using System.Text;
using Rask.Flux;

// The Flux UI harness: `dotnet run --project scripts/flux/Flux -- <command> [args]`. Each command is one class
// marked [FluxCommand("name")] (README.md); this finds them, so a new check is a new file and nothing else.
Console.OutputEncoding = Encoding.UTF8;
var commands = Assembly.GetExecutingAssembly().GetTypes()
    .Select(type => (type, mark: type.GetCustomAttribute<FluxCommandAttribute>()))
    .Where(found => found.mark is not null && typeof(IFluxCommand).IsAssignableFrom(found.type))
    .ToDictionary(found => found.mark!.Name, found => (found.type, found.mark!), StringComparer.Ordinal);

// The name a script had is the name its command has: `parity.mjs` is `parity`.
var asked = args.Length > 0 ? Path.GetFileNameWithoutExtension(args[0]) : "";
if (!commands.TryGetValue(asked, out var command))
{
    Console.Error.WriteLine(asked.Length == 0 ? "usage: dotnet run --project scripts/flux/Flux -- <command> [args]" : $"flux: no command \"{asked}\"");
    foreach (var (name, (_, mark)) in commands.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"  {name,-18} {mark.Summary}");
    }

    return 1;
}

try
{
    var instance = (IFluxCommand)Activator.CreateInstance(command.type)!;
    return await instance.Run(args[1..]);
}
catch (FluxExit exit)
{
    if (exit.Message.Length > 0)
    {
        Console.Error.WriteLine(exit.Message);
    }

    return exit.Code;
}
